using System.Net.Http;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using shmoxy.api.ipc;
using shmoxy.api.models;
using shmoxy.api.server;

namespace shmoxy.api.Controllers;

[ApiController]
[Route("api/proxies/{proxyId}/certs/root")]
public class CertsController : ControllerBase
{
    private readonly IProxyProcessManager _processManager;
    private readonly IRemoteProxyRegistry _registry;
    private readonly ILogger<CertsController> _logger;

    public CertsController(
        IProxyProcessManager processManager,
        IRemoteProxyRegistry registry,
        ILogger<CertsController> logger)
    {
        _processManager = processManager;
        _registry = registry;
        _logger = logger;
    }

    /// <summary>
    /// Download root CA certificate in specified format.
    /// </summary>
    /// <param name="proxyId">"local" for local proxy, or remote proxy GUID</param>
    /// <param name="type">Certificate format: pem (default) or der</param>
    [HttpGet]
    public async Task<IActionResult> GetRootCertificate(
        string proxyId,
        [FromQuery] string type = "pem",
        CancellationToken ct = default)
    {
        var format = type.ToLowerInvariant();
        if (format is not ("pem" or "der"))
        {
            return BadRequest(new { Message = "Invalid type. Must be 'pem' or 'der'" });
        }

        if (proxyId.Equals("local", StringComparison.OrdinalIgnoreCase))
        {
            return await GetLocalCertificateAsync(format, ct);
        }
        else
        {
            return await GetRemoteCertificateAsync(proxyId, format, ct);
        }
    }

    private async Task<IActionResult> GetLocalCertificateAsync(string format, CancellationToken ct)
    {
        var state = await _processManager.GetStateAsync();
        if (state?.State != ProxyProcessState.Running)
        {
            return BadRequest(new { Message = "Local proxy must be running to download certificates" });
        }

        try
        {
            var certBytes = format switch
            {
                "pem" => Encoding.UTF8.GetBytes(await _processManager.GetRootCertPemAsync(ct)),
                "der" => await _processManager.GetRootCertDerAsync(ct),
                _ => throw new InvalidOperationException($"Unsupported format: {format}")
            };

            var contentType = format == "pem" ? "text/plain" : "application/x-x509-ca-cert";
            var filename = $"shmoxy-root-ca.{format}";
            return File(certBytes, contentType, filename);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download certificate from local proxy");
            return StatusCode(500, new { Message = "Failed to retrieve certificate from local proxy" });
        }
    }

    private async Task<IActionResult> GetRemoteCertificateAsync(string proxyId, string format, CancellationToken ct)
    {
        var proxy = await _registry.GetByIdAsync(proxyId, ct);
        if (proxy == null)
        {
            return NotFound(new { Message = $"Remote proxy {proxyId} not found" });
        }

        var handler = new HttpClientHandler();
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(proxy.AdminUrl),
            Timeout = TimeSpan.FromSeconds(5)
        };
        httpClient.DefaultRequestHeaders.Add("X-API-Key", proxy.ApiKey);

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var tempLogger = loggerFactory.CreateLogger<ProxyIpcClient>();
        var tempClient = new ProxyIpcClient(httpClient, tempLogger);

        try
        {
            var certBytes = format switch
            {
                "pem" => Encoding.UTF8.GetBytes(await tempClient.GetRootCertPemAsync(ct)),
                "der" => await tempClient.GetRootCertDerAsync(ct),
                _ => throw new InvalidOperationException($"Unsupported format: {format}")
            };

            var contentType = format == "pem" ? "text/plain" : "application/x-x509-ca-cert";
            var filename = $"shmoxy-root-ca.{format}";
            return File(certBytes, contentType, filename);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download certificate from remote proxy {ProxyId}", proxyId);
            return StatusCode(502, new { Message = "Failed to retrieve certificate from remote proxy" });
        }
    }
}
