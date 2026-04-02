using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using shmoxy.api.ipc;
using shmoxy.api.models;
using shmoxy.api.server;
using shmoxy.shared.ipc;

namespace shmoxy.api.Controllers;

[ApiController]
[Route("api/proxies/{proxyId}/inspect")]
public class InspectionController : ControllerBase
{
    private readonly IProxyProcessManager _processManager;
    private readonly IRemoteProxyRegistry _registry;
    private readonly ILogger<InspectionController> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;

    public InspectionController(
        IProxyProcessManager processManager,
        IRemoteProxyRegistry registry,
        ILogger<InspectionController> logger,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory)
    {
        _processManager = processManager;
        _registry = registry;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Stream inspection events via Server-Sent Events (SSE).
    /// </summary>
    /// <param name="proxyId">"local" for local proxy, or remote proxy GUID</param>
    /// <param name="ct">Cancellation token</param>
    [HttpGet("stream")]
    public async Task GetStream(string proxyId, CancellationToken ct)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        try
        {
            IAsyncEnumerable<InspectionEvent> eventStream = proxyId.Equals("local", StringComparison.OrdinalIgnoreCase)
                ? GetLocalStream(ct)
                : GetRemoteStream(proxyId, ct);

            await foreach (var evt in eventStream)
            {
                var json = JsonSerializer.Serialize(evt);
                await Response.WriteAsync($"data: {json}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on client disconnect - log at debug level only
            _logger.LogDebug("Inspection stream disconnected for proxy {ProxyId}", proxyId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in inspection stream for proxy {ProxyId}", proxyId);
        }
    }

    private async IAsyncEnumerable<InspectionEvent> GetLocalStream([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var state = await _processManager.GetStateAsync();
        if (state?.State != ProxyProcessState.Running)
        {
            throw new InvalidOperationException("Local proxy must be running to stream inspection events");
        }

        var client = _processManager.GetIpcClient();

        // Ensure inspection is enabled before streaming
        await client.EnableInspectionAsync(ct);

        await foreach (var evt in client.GetInspectionStreamAsync(ct))
        {
            yield return evt;
        }
    }

    private async IAsyncEnumerable<InspectionEvent> GetRemoteStream(string proxyId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var proxy = await _registry.GetByIdAsync(proxyId, ct);
        if (proxy == null)
        {
            throw new InvalidOperationException($"Remote proxy {proxyId} not found");
        }

        var httpClient = _httpClientFactory.CreateClient();
        httpClient.BaseAddress = new Uri(proxy.AdminUrl);
        httpClient.Timeout = TimeSpan.FromSeconds(5);
        httpClient.DefaultRequestHeaders.Add("X-API-Key", proxy.ApiKey);

        var logger = _loggerFactory.CreateLogger<ProxyIpcClient>();
        var client = new ProxyIpcClient(httpClient, logger);

        try
        {
            await foreach (var evt in client.GetInspectionStreamAsync(ct))
            {
                yield return evt;
            }
        }
        finally
        {
            httpClient.Dispose();
        }
    }
}
