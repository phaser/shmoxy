using System.Net;
using System.Text;
using shmoxy.server;
using shmoxy.server.hooks;
using shmoxy.shared.ipc;
using Xunit;

namespace shmoxy.e2e;

[Trait("Category", "Integration")]
public class ResponseInspectionTests : IAsyncLifetime
{
    private ProxyServer? _server;
    private InspectionHook? _inspectionHook;
    private CancellationTokenSource? _cts;

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
    }

    public async Task DisposeAsync()
    {
        if (_cts != null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
        }
        _server?.Dispose();
        _inspectionHook?.Dispose();
    }

    [Fact]
    public async Task ResponseInspection_HttpsRequest_CapturesHeadersAndBody()
    {
        var port = _server!.ListeningPort;
        var proxy = new WebProxy($"http://localhost:{port}");
        using var handler = new HttpClientHandler
        {
            Proxy = proxy,
            UseProxy = true,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };

        try
        {
            await client.GetAsync("https://example.com");
        }
        catch
        {
            // May fail due to cert trust but inspection events should still be captured
        }

        await Task.Delay(1000);

        var reader = _inspectionHook!.GetReader();
        var events = new List<InspectionEvent>();
        while (reader.TryRead(out var evt))
            events.Add(evt);

        var responseEvents = events.Where(e => e.EventType == "response").ToList();
        Assert.NotEmpty(responseEvents);

        var response = responseEvents.First();
        Assert.NotEmpty(response.Headers);
        Assert.NotNull(response.Body);
        Assert.True(response.Body.Length > 0, "Response body should not be empty");

        var bodyText = Encoding.ASCII.GetString(response.Body, 0, Math.Min(response.Body.Length, 100));
        Assert.DoesNotContain("HTTP/1.1", bodyText);
    }

    [Fact]
    public async Task ResponseInspection_HttpRequest_CapturesHeadersAndBody()
    {
        var port = _server!.ListeningPort;
        var proxy = new WebProxy($"http://localhost:{port}");
        using var handler = new HttpClientHandler { Proxy = proxy, UseProxy = true };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };

        await client.GetAsync("http://example.com");

        await Task.Delay(1000);

        var reader = _inspectionHook!.GetReader();
        var events = new List<InspectionEvent>();
        while (reader.TryRead(out var evt))
            events.Add(evt);

        var responseEvents = events.Where(e => e.EventType == "response").ToList();
        Assert.NotEmpty(responseEvents);

        var response = responseEvents.First();
        Assert.NotEmpty(response.Headers);
        Assert.NotNull(response.Body);
        Assert.True(response.Body.Length > 0, "Response body should not be empty");
    }
}
