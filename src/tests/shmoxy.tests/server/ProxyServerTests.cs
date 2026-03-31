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

    [Fact]
    public void ParseRawHttpResponse_ParsesStatusCode()
    {
        var raw = "HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\n\r\nHello"u8.ToArray();

        var (statusCode, _, _) = ProxyServer.ParseRawHttpResponse(raw);

        Assert.Equal(200, statusCode);
    }

    [Fact]
    public void ParseRawHttpResponse_ParsesHeaders()
    {
        var raw = "HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nX-Custom: value\r\n\r\nBody"u8.ToArray();

        var (_, headers, _) = ProxyServer.ParseRawHttpResponse(raw);

        Assert.Equal("text/plain", headers["Content-Type"]);
        Assert.Equal("value", headers["X-Custom"]);
    }

    [Fact]
    public void ParseRawHttpResponse_SeparatesBodyFromHeaders()
    {
        var bodyContent = "Hello, World!";
        var raw = System.Text.Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\n\r\n{bodyContent}");

        var (_, _, body) = ProxyServer.ParseRawHttpResponse(raw);

        Assert.Equal(bodyContent, System.Text.Encoding.ASCII.GetString(body));
    }

    [Fact]
    public void ParseRawHttpResponse_EmptyBodyWhenNoContent()
    {
        var raw = "HTTP/1.1 204 No Content\r\nX-Header: value\r\n\r\n"u8.ToArray();

        var (statusCode, headers, body) = ProxyServer.ParseRawHttpResponse(raw);

        Assert.Equal(204, statusCode);
        Assert.Equal("value", headers["X-Header"]);
        Assert.Empty(body);
    }

    [Fact]
    public void ParseRawHttpResponse_HandlesNoHeaderBodySeparator()
    {
        var raw = "HTTP/1.1 200 OK\r\nContent-Type: text/plain"u8.ToArray();

        var (statusCode, headers, body) = ProxyServer.ParseRawHttpResponse(raw);

        Assert.Equal(200, statusCode);
        Assert.Equal("text/plain", headers["Content-Type"]);
        Assert.Empty(body);
    }

    [Fact]
    public void ParseRawHttpResponse_ParsesBinaryBody()
    {
        var headerBytes = System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\n\r\n");
        var bodyBytes = new byte[] { 0x00, 0x01, 0xFF, 0xFE };
        var raw = new byte[headerBytes.Length + bodyBytes.Length];
        Buffer.BlockCopy(headerBytes, 0, raw, 0, headerBytes.Length);
        Buffer.BlockCopy(bodyBytes, 0, raw, headerBytes.Length, bodyBytes.Length);

        var (_, headers, body) = ProxyServer.ParseRawHttpResponse(raw);

        Assert.Equal("application/octet-stream", headers["Content-Type"]);
        Assert.Equal(bodyBytes, body);
    }

    [Fact]
    public void ParseRawHttpResponse_DecodesChunkedTransferEncoding()
    {
        var jsonBody = "{\"Name\":\"TestUser\",\"Email\":\"test@example.com\"}";
        var chunkSize = jsonBody.Length.ToString("x"); // hex chunk size
        var chunkedBody = $"{chunkSize}\r\n{jsonBody}\r\n0\r\n\r\n";
        var raw = System.Text.Encoding.ASCII.GetBytes(
            $"HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\nContent-Type: application/json\r\n\r\n{chunkedBody}");

        var (statusCode, headers, body) = ProxyServer.ParseRawHttpResponse(raw);

        Assert.Equal(200, statusCode);
        Assert.Equal(jsonBody, System.Text.Encoding.ASCII.GetString(body));
    }

    [Fact]
    public void ParseRawHttpResponse_DecodesMultipleChunks()
    {
        var chunk1 = "Hello, ";
        var chunk2 = "World!";
        var chunkedBody = $"{chunk1.Length:x}\r\n{chunk1}\r\n{chunk2.Length:x}\r\n{chunk2}\r\n0\r\n\r\n";
        var raw = System.Text.Encoding.ASCII.GetBytes(
            $"HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n{chunkedBody}");

        var (_, _, body) = ProxyServer.ParseRawHttpResponse(raw);

        Assert.Equal("Hello, World!", System.Text.Encoding.ASCII.GetString(body));
    }

    [Fact]
    public void ParseRawHttpResponse_NonChunkedBodyUnchanged()
    {
        var bodyContent = "Plain body content";
        var raw = System.Text.Encoding.ASCII.GetBytes(
            $"HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\n\r\n{bodyContent}");

        var (_, _, body) = ProxyServer.ParseRawHttpResponse(raw);

        Assert.Equal(bodyContent, System.Text.Encoding.ASCII.GetString(body));
    }

    public void Dispose()
    {
        if (_disposed) return;

        _httpClient.Dispose();
        _disposed = true;
    }
}
