using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;

namespace shmoxy.server;

/// <summary>
/// Handles HTTP requests through a proxy tunnel.
/// Creates HTTPS tunnels using CONNECT method and forwards HTTP traffic.
/// </summary>
public class ProxyHttpClient : IDisposable
{
    private readonly TcpClient _client;
    private NetworkStream? _stream;
    private bool _tunnelEstablished;
    private bool _disposed;

    /// <summary>
    /// Creates a proxy client connected to the target server.
    /// Establishes an HTTPS tunnel via CONNECT if needed.
    /// </summary>
    public ProxyHttpClient(TcpClient client, string host, int port, bool useTunnel = true)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        Host = host;
        Port = port;

        if (useTunnel && port != 80)
            EstablishTunnel();
    }

    /// <summary>
    /// Gets the target hostname.
    /// </summary>
    public string Host { get; }

    /// <summary>
    /// Gets the target port.
    /// </summary>
    public int Port { get; }

    /// <summary>
    /// Gets whether a tunnel was established via CONNECT.
    /// </summary>
    public bool TunnelEstablished => _tunnelEstablished;

    /// <summary>
    /// Gets the underlying network stream for reading/writing.
    /// </summary>
    public NetworkStream Stream => _stream ??= _client.GetStream();

    /// <summary>
    /// Establishes an HTTPS tunnel using the CONNECT method.
    /// </summary>
    private void EstablishTunnel()
    {
        var connectRequest = Encoding.ASCII.GetBytes($"CONNECT {Host}:{Port} HTTP/1.1\r\nHost: {Host}:{Port}\r\nConnection: Keep-Alive\r\n\r\n");

        Stream.Write(connectRequest, 0, connectRequest.Length);
        Stream.Flush();

        // Read the response
        var response = ReadResponse();

        if (!response.StartsWith("HTTP/1.1 200"))
            throw new IOException($"Failed to establish tunnel: {response}");

        _tunnelEstablished = true;
    }

    /// <summary>
    /// Sends an HTTP request through the proxy and returns the response.
    /// </summary>
    public async Task<byte[]> SendRequestAsync(string method, string path, Dictionary<string, string> headers, byte[]? body = null)
    {
        var requestBuilder = new StringBuilder();
        requestBuilder.AppendLine($"{method} {path} HTTP/1.1");
        requestBuilder.AppendLine($"Host: {Host}:{Port}");

        foreach (var header in headers)
            requestBuilder.AppendLine($"{header.Key}: {header.Value}");

        if (body != null && body.Length > 0)
            requestBuilder.AppendLine($"Content-Length: {body.Length}");

        requestBuilder.Append("\r\n");

        var requestBytes = Encoding.ASCII.GetBytes(requestBuilder.ToString());
        Stream.Write(requestBytes, 0, requestBytes.Length);

        if (body != null && body.Length > 0)
            await Stream.WriteAsync(body, 0, body.Length);

        return await ReadResponseAsync();
    }

    /// <summary>
    /// Reads the HTTP response status line and headers.
    /// </summary>
    private string ReadResponse()
    {
        var result = new StringBuilder();
        var buffer = new byte[1024];

        while (true)
        {
            var bytesRead = Stream.Read(buffer, 0, buffer.Length);
            if (bytesRead == 0) break;

            var chunk = Encoding.ASCII.GetString(buffer, 0, bytesRead);
            result.Append(chunk);

            if (chunk.Contains("\r\n\r\n")) break;
        }

        return result.ToString();
    }

    /// <summary>
    /// Reads the full HTTP response including body.
    /// </summary>
    public async Task<byte[]> ReadResponseAsync()
    {
        var result = new MemoryStream();
        var buffer = new byte[8192];

        // First read headers to get Content-Length
        var headerBuffer = new StringBuilder();
        while (true)
        {
            var bytesRead = await Stream.ReadAsync(buffer, 0, buffer.Length);
            if (bytesRead == 0) break;

            var chunk = Encoding.ASCII.GetString(buffer, 0, bytesRead);
            headerBuffer.Append(chunk);
            result.Write(buffer, 0, bytesRead);

            if (chunk.Contains("\r\n\r\n")) break;
        }

        // Parse Content-Length from headers
        var headersComplete = headerBuffer.ToString();
        int contentLength = -1;

        var headersEnd = headersComplete.IndexOf("\r\n\r\n");
        if (headersEnd > 0)
        {
            var headerSection = headersComplete.Substring(0, headersEnd);
            foreach (var line in headerSection.Split('\n'))
            {
                if (line.ToLowerInvariant().StartsWith("content-length:"))
                {
                    if (int.TryParse(line.Substring("Content-Length:".Length).Trim(), out var length))
                        contentLength = length;
                    break;
                }
            }
        }

        // Read body based on Content-Length or until connection closes
        while (true)
        {
            if (contentLength >= 0 && result.Length - headersEnd - 4 >= contentLength)
                break;

            var bytesRead = await Stream.ReadAsync(buffer, 0, buffer.Length);
            if (bytesRead == 0) break;

            result.Write(buffer, 0, bytesRead);
        }

        return result.ToArray();
    }

    /// <summary>
    /// Reads a specific number of bytes from the response.
    /// </summary>
    public async Task<byte[]> ReadAsync(int count)
    {
        var buffer = new byte[count];
        var totalRead = 0;

        while (totalRead < count)
        {
            var bytesRead = await Stream.ReadAsync(buffer, totalRead, count - totalRead);
            if (bytesRead == 0) break;
            totalRead += bytesRead;
        }

        return buffer.Take(totalRead).ToArray();
    }

    public void Dispose()
    {
        if (_disposed) return;

        _stream?.Dispose();
        _client.Dispose();
        _disposed = true;
    }
}
