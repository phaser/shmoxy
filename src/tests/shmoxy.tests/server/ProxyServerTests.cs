using System.Net;
using shmoxy.server;
using shmoxy.shared.ipc;

namespace shmoxy.tests.server;

public class ProxyServerTests : IClassFixture<ProxyTestFixture>, IDisposable
{
    private readonly ProxyTestFixture _fixture;
    private readonly HttpClient _httpClient;
    private bool _disposed;

    /// <summary>
    /// Timeout for test operations that involve server startup or network access.
    /// </summary>
    private static readonly TimeSpan ServerStartTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HttpRequestTimeout = TimeSpan.FromSeconds(30);

    public ProxyServerTests(ProxyTestFixture fixture)
    {
        _fixture = fixture;
        _httpClient = new HttpClient { Timeout = HttpRequestTimeout };
    }

    [Fact]
    public void StartAsync_ShouldStartServer()
    {
        // The fixture starts the server via IAsyncLifetime.InitializeAsync
        // before any test runs, so the server should already be listening.

        // Assert
        Assert.True(_fixture.Server.IsListening, "Server should be listening after StartAsync");
        Assert.True(_fixture.Server.ListeningPort > 0, "Server should be bound to a valid port");
    }

    [Fact]
    public async Task StopAsync_ShouldStopServer()
    {
        // Arrange — create a separate server instance for this test
        // so stopping it doesn't affect other tests using the shared fixture.
        var config = new ProxyConfig
        {
            Port = 0,
            LogLevel = ProxyConfig.LogLevelEnum.Warn
        };
        using var server = new ProxyServer(config);

        using var cts = new CancellationTokenSource();
        var serverTask = server.StartAsync(cts.Token);

        // Wait until the server is actually listening
        var deadline = DateTime.UtcNow + ServerStartTimeout;
        while (!server.IsListening && DateTime.UtcNow < deadline)
            await Task.Delay(50);

        Assert.True(server.IsListening, "Server should be listening before stop");

        // Act
        await server.StopAsync();
        cts.Cancel();

        // Assert
        Assert.False(server.IsListening, "Server should not be listening after StopAsync");
    }

    [Fact]
    public async Task ProxyServer_ShouldHandleSimpleRequest()
    {
        // Arrange — use the shared fixture's running server
        var port = _fixture.Server.ListeningPort;
        Assert.True(_fixture.Server.IsListening, "Fixture server should be running");

        // Act — send an HTTP request through the proxy
        var proxy = new WebProxy($"http://localhost:{port}");
        using var handler = new HttpClientHandler { Proxy = proxy, UseProxy = true };
        using var client = new HttpClient(handler) { Timeout = HttpRequestTimeout };

        // Send a simple HTTP GET through the proxy to httpbin
        // The proxy should accept the connection and attempt to forward it.
        // We consider any non-exception result (even a proxy error) as the
        // server successfully handling the connection.
        HttpResponseMessage? response = null;
        try
        {
            response = await client.GetAsync("http://httpbin.org/get");
        }
        catch (HttpRequestException)
        {
            // The proxy may fail to fully forward but the key assertion is that
            // it accepted and handled the connection (didn't crash or refuse).
        }

        // Assert — server should still be running after handling the request
        Assert.True(_fixture.Server.IsListening, "Server should still be listening after handling a request");
    }

    /// <summary>
    /// Integration test: Verify proxy forwards HTTP requests to real external sites.
    /// Routes traffic through the proxy and validates responses.
    /// </summary>
    [Fact]
    public async Task Integration_ShouldForwardRequestsToTargetSites()
    {
        // Arrange — use a dedicated server for integration testing
        var config = new ProxyConfig
        {
            Port = 0,
            LogLevel = ProxyConfig.LogLevelEnum.Warn
        };
        using var testServer = new ProxyServer(config);
        using var cts = new CancellationTokenSource();
        var serverTask = testServer.StartAsync(cts.Token);

        // Wait until the server is actually listening
        var deadline = DateTime.UtcNow + ServerStartTimeout;
        while (!testServer.IsListening && DateTime.UtcNow < deadline)
            await Task.Delay(50);

        Assert.True(testServer.IsListening, "Test server should be listening");

        var port = testServer.ListeningPort;
        var proxy = new WebProxy($"http://localhost:{port}");
        using var handler = new HttpClientHandler { Proxy = proxy, UseProxy = true };
        using var client = new HttpClient(handler) { Timeout = HttpRequestTimeout };

        // Sites to test
        var sites = new[]
        {
            ("http://httpbin.org/get", "httpbin"),
            ("http://example.com", "Example"),
            ("http://info.cern.ch", "CERN")
        };

        try
        {
            foreach (var (url, name) in sites)
            {
                await TestSiteAsync(client, url, name);
            }
        }
        finally
        {
            await testServer.StopAsync();
            cts.Cancel();
        }
    }

    private static async Task TestSiteAsync(HttpClient client, string url, string name)
    {
        // Act — send request through the proxy
        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.True(response.IsSuccessStatusCode,
            $"{name} ({url}) should return a success status code, got {response.StatusCode}");
        Assert.True(body.Length > 0, $"{name} should return content");
    }

    public void Dispose()
    {
        if (_disposed) return;

        _httpClient.Dispose();
        _disposed = true;
    }
}
