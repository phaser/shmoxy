using Xunit;
using shmoxy;
using System.Net.Sockets;
using System.Text;

namespace shmoxy.tests;

/// <summary>
/// Unit tests for InfoPageHandler.
/// </summary>
public class InfoPageHandlerTests : IClassFixture<ProxyTestFixture>, IDisposable
{
    private readonly ProxyTestFixture _fixture;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private bool _disposed;

    public InfoPageHandlerTests(ProxyTestFixture fixture)
    {
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
    }

    /// <summary>
    /// Reads the full response from a NetworkStream until the server closes the connection.
    /// The server sends Connection: close, so EOF signals the end of the response.
    /// </summary>
    private static async Task<string> ReadFullResponseAsync(NetworkStream stream)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[8192];
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
        {
            ms.Write(buffer, 0, bytesRead);
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenConfigIsNull()
    {
        // Arrange & Act
        using var tlsHandler = new TlsHandler();
        var exception = Record.Exception(() => new InfoPageHandler(null!, tlsHandler, DateTime.UtcNow));

        // Assert
        Assert.IsType<ArgumentNullException>(exception);
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenTlsHandlerIsNull()
    {
        // Arrange & Act
        var exception = Record.Exception(() => new InfoPageHandler(_fixture.Config, null!, DateTime.UtcNow));

        // Assert
        Assert.IsType<ArgumentNullException>(exception);
    }

    [Fact]
    public async Task HandleAsync_RootPath_ReturnsHtmlWithProxyInfo()
    {
        // Arrange
        _client = new TcpClient();
        await _client.ConnectAsync("localhost", _fixture.Server.ListeningPort);
        _stream = _client.GetStream();

        var request = "GET / HTTP/1.1\r\nHost: localhost\r\n\r\n";
        await _stream.WriteAsync(Encoding.ASCII.GetBytes(request));

        // Act
        var response = await ReadFullResponseAsync(_stream);

        // Assert
        Assert.Contains("HTTP/1.1 200 OK", response);
        Assert.Contains("Shmoxy Proxy Server", response);
        Assert.Contains($"Listening Port</th><td>{_fixture.Server.ListeningPort}", response);
    }

    [Fact]
    public async Task HandleAsync_PemDownload_ReturnsPemCertificate()
    {
        // Arrange
        _client = new TcpClient();
        await _client.ConnectAsync("localhost", _fixture.Server.ListeningPort);
        _stream = _client.GetStream();

        var request = "GET /shmoxy-ca.pem HTTP/1.1\r\nHost: localhost\r\n\r\n";
        await _stream.WriteAsync(Encoding.ASCII.GetBytes(request));

        // Act
        var response = await ReadFullResponseAsync(_stream);

        // Assert
        Assert.Contains("HTTP/1.1 200 OK", response);
        Assert.Contains("Content-Type: application/x-pem-file", response);
        Assert.Contains("-----BEGIN CERTIFICATE-----", response);
        Assert.Contains("-----END CERTIFICATE-----", response);
    }

    [Fact]
    public async Task HandleAsync_CrtDownload_ReturnsDerCertificate()
    {
        // Arrange
        _client = new TcpClient();
        await _client.ConnectAsync("localhost", _fixture.Server.ListeningPort);
        _stream = _client.GetStream();

        var request = "GET /shmoxy-ca.crt HTTP/1.1\r\nHost: localhost\r\n\r\n";
        await _stream.WriteAsync(Encoding.ASCII.GetBytes(request));

        // Act
        var response = await ReadFullResponseAsync(_stream);

        // Assert
        Assert.Contains("HTTP/1.1 200 OK", response);
        Assert.Contains("Content-Type: application/vnd.ms-pkiseccert", response);
        Assert.Contains("Content-Disposition: attachment; filename=\"shmoxy-ca.crt\"", response);
    }

    [Fact]
    public async Task HandleAsync_UnknownPath_Returns404()
    {
        // Arrange
        _client = new TcpClient();
        await _client.ConnectAsync("localhost", _fixture.Server.ListeningPort);
        _stream = _client.GetStream();

        var request = "GET /nonexistent HTTP/1.1\r\nHost: localhost\r\n\r\n";
        await _stream.WriteAsync(Encoding.ASCII.GetBytes(request));

        // Act
        var response = await ReadFullResponseAsync(_stream);

        // Assert
        Assert.Contains("HTTP/1.1 404 Not Found", response);
    }

    [Fact]
    public async Task HandleAsync_HtmlContainsInstallInstructions()
    {
        // Arrange
        _client = new TcpClient();
        await _client.ConnectAsync("localhost", _fixture.Server.ListeningPort);
        _stream = _client.GetStream();

        var request = "GET / HTTP/1.1\r\nHost: localhost\r\n\r\n";
        await _stream.WriteAsync(Encoding.ASCII.GetBytes(request));

        // Act
        var response = await ReadFullResponseAsync(_stream);

        // Assert
        Assert.Contains("Installation Instructions", response);
        Assert.Contains("Windows", response);
        Assert.Contains("macOS", response);
        Assert.Contains("Linux", response);
        Assert.Contains("Chrome", response);
        Assert.Contains("Firefox", response);
        Assert.Contains("certutil -addstore", response);
    }

    [Fact]
    public void Dispose_DisposesWithoutError()
    {
        // Arrange
        using var tlsHandler = new TlsHandler();
        var handler = new InfoPageHandler(_fixture.Config, tlsHandler, DateTime.UtcNow);

        // Act & Assert - should not throw
        var exception = Record.Exception(handler.Dispose);

        // Assert
        Assert.Null(exception);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _stream?.Dispose();
        _client?.Dispose();
        _disposed = true;
    }
}
