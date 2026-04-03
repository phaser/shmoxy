using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using shmoxy.ipc;
using shmoxy.server;
using shmoxy.server.hooks;
using shmoxy.shared.ipc;
using Xunit;

namespace shmoxy.e2e;

/// <summary>
/// Tests that response inspection data (headers/body) survives the full SSE pipeline:
/// InspectionHook → IPC SSE → consumer
/// </summary>
[Trait("Category", "Integration")]
public class ResponseInspectionSseTests : IAsyncLifetime
{
    private ProxyServer? _server;
    private InspectionHook? _inspectionHook;
    private CancellationTokenSource? _cts;
    private IHost? _ipcHost;
    private int _ipcPort;

    public async Task InitializeAsync()
    {
        var config = new ProxyConfig { Port = 0, LogLevel = ProxyConfig.LogLevelEnum.Debug };
        _inspectionHook = new InspectionHook();
        var hookChain = new InterceptHookChain().Add(_inspectionHook);

        _server = new ProxyServer(config, hookChain);
        _cts = new CancellationTokenSource();

        _ = _server.StartAsync(_cts.Token);

        for (var i = 0; i < 20 && !_server.IsListening; i++)
            await Task.Delay(50);

        if (!_server.IsListening)
            throw new InvalidOperationException("Proxy server failed to start");

        _inspectionHook.Enabled = true;

        // Start an IPC host with the inspect/stream SSE endpoint
        var stateService = new ProxyStateService(_server, _inspectionHook);
        var apiKeyService = new ApiKeyService();

        _ipcHost = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseKestrel(k => k.ListenAnyIP(0))
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton(stateService);
                    services.AddSingleton(apiKeyService);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        var loggerFactory = app.ApplicationServices.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>();
                        endpoints.MapProxyControlApi(stateService, config, loggerFactory);
                    });
                });
            })
            .Build();

        await _ipcHost.StartAsync(_cts.Token);

        var addresses = _ipcHost.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();
        var address = addresses?.Addresses.First() ?? throw new InvalidOperationException("No address bound");
        _ipcPort = new Uri(address).Port;
    }

    public async Task DisposeAsync()
    {
        if (_ipcHost != null)
        {
            await _ipcHost.StopAsync();
            _ipcHost.Dispose();
        }

        if (_cts != null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
        }
        if (_server != null)
            await _server.DisposeAsync();
        _inspectionHook?.Dispose();
    }

    [Fact]
    public async Task SsePipeline_ResponseHeaders_SurviveSerialization()
    {
        // Make an HTTP request through the proxy first
        var port = _server!.ListeningPort;
        var proxy = new WebProxy($"http://localhost:{port}");
        using var handler = new HttpClientHandler { Proxy = proxy, UseProxy = true };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        await client.GetAsync("http://example.com");

        // Give inspection a moment to capture
        await Task.Delay(500);

        // Now connect to the SSE stream and read events
        // The events are buffered in the Channel, so connecting after the request still works
        using var sseClient = new HttpClient { BaseAddress = new Uri($"http://localhost:{_ipcPort}"), Timeout = Timeout.InfiniteTimeSpan };
        using var sseRequest = new HttpRequestMessage(HttpMethod.Get, "/ipc/inspect/stream");
        using var sseResponse = await sseClient.SendAsync(sseRequest, HttpCompletionOption.ResponseHeadersRead);
        using var sseStream = await sseResponse.Content.ReadAsStreamAsync();
        using var sseReader = new StreamReader(sseStream, Encoding.UTF8);

        var events = new List<InspectionEvent>();
        using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        try
        {
            while (!readCts.Token.IsCancellationRequested)
            {
                var line = await sseReader.ReadLineAsync(readCts.Token);
                if (line == null) break;

                if (line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    var json = line["data: ".Length..];
                    var evt = JsonSerializer.Deserialize<InspectionEvent>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    if (evt != null)
                    {
                        events.Add(evt);
                        // We have a response event — stop reading
                        if (evt.EventType == "response")
                            break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout reading SSE — proceed with what we have
        }

        // Verify response event has headers and body after going through SSE
        var responseEvents = events.Where(e => e.EventType == "response").ToList();
        Assert.NotEmpty(responseEvents);

        var response = responseEvents.First();
        Assert.NotEmpty(response.Headers);
        Assert.NotNull(response.Body);
        Assert.True(response.Body.Length > 0, $"Response body should not be empty. Headers count: {response.Headers.Count}");
    }
}
