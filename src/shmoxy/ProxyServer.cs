using System;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Channels;

namespace shmoxy;

/// <summary>
/// Configuration for the proxy server.
/// </summary>
public class ProxyConfig
{
    public int Port { get; set; } = 8080;
    public string? CertPath { get; set; }
    public string? KeyPath { get; set; }
    public LogLevelEnum LogLevel { get; set; } = LogLevelEnum.Info;

    /// <summary>
    /// Logging levels.
    /// </summary>
    public enum LogLevelEnum
    {
        Debug,
        Info,
        Warn,
        Error
    }
}

/// <summary>
/// Core proxy server that handles HTTP/HTTPS requests with TLS termination.
/// </summary>
public class ProxyServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly TlsHandler _tlsHandler;
    private readonly IInterceptHook _interceptor;
    private readonly ProxyConfig _config;
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private bool _disposed;
    private bool _isListening;
    private readonly DateTime _startTime;
    private InfoPageHandler? _infoPageHandler;

    /// <summary>
    /// Gets whether the server is currently listening for connections.
    /// </summary>
    public bool IsListening => _isListening;

    /// <summary>
    /// Gets the actual port the server is listening on.
    /// Useful when configured with port 0 (OS-assigned).
    /// Only valid after <see cref="StartAsync"/> has been called.
    /// </summary>
    public int ListeningPort => ((System.Net.IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>
    /// Gets the start time of the proxy server, used to calculate uptime.
    /// </summary>
    public DateTime StartTime => _startTime;

    /// <summary>
    /// Creates a new proxy server with default configuration.
    /// </summary>
    public ProxyServer() : this(new ProxyConfig()) { }

    /// <summary>
    /// Creates a new proxy server with the specified configuration.
    /// </summary>
    public ProxyServer(ProxyConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _listener = TcpListener.Create(config.Port);
        _tlsHandler = new TlsHandler();
        _interceptor = new NoOpInterceptHook();
        _startTime = DateTime.UtcNow;

        Log(ProxyConfig.LogLevelEnum.Info, $"Proxy server initialized on port {config.Port}");
    }

    /// <summary>
    /// Creates a proxy server with custom interceptor.
    /// </summary>
    public ProxyServer(ProxyConfig config, IInterceptHook interceptor) : this(config)
    {
        _interceptor = interceptor ?? throw new ArgumentNullException(nameof(interceptor));
    }

    /// <summary>
    /// Checks if a request is directed at the proxy itself.
    /// A request is "for self" when:
    /// - It uses a relative path (not absolute URL) -- the client connected directly to us
    /// - OR it uses an absolute URL targeting localhost/127.0.0.1/::1 on our listening port
    /// </summary>
    private bool IsRequestForSelf(string host, int port, string path)
    {
        // Absolute URL like "GET http://localhost:8080/ HTTP/1.1"
        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
            path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var uri = new Uri(path);
                var uriHost = uri.Host.ToLowerInvariant();
                var uriPort = uri.IsDefaultPort ? 80 : uri.Port;

                if (uriPort == ListeningPort && IsLoopbackHost(uriHost))
                    return true;
            }
            catch { /* Invalid URL, not for us */ }

            return false;
        }

        // Relative path like "GET / HTTP/1.1" with Host header
        // If the host is a loopback address, this is for us.
        // For relative paths, the client connected directly to our listener,
        // so if Host matches loopback, it's definitely addressed to us.
        return IsLoopbackHost(host.ToLowerInvariant());
    }

    private static bool IsLoopbackHost(string host)
    {
        return host == "localhost" || 
               host == "127.0.0.1" || 
               host == "::1";
    }

    /// <summary>
    /// Starts listening for incoming connections.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_cts != null && !_cts.IsCancellationRequested)
            return;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var combinedCts = _cts;

        try
        {
            _listener.Start();
            _isListening = true;
            Log(ProxyConfig.LogLevelEnum.Info, "Proxy server started");

            while (!combinedCts.Token.IsCancellationRequested)
            {
                var clientTask = _listener.AcceptTcpClientAsync();

                await Task.WhenAny(clientTask, Task.Delay(-1, combinedCts.Token));

                if (clientTask.Status == TaskStatus.RanToCompletion && !combinedCts.Token.IsCancellationRequested)
                {
                    var client = await clientTask;
                    _ = HandleConnectionAsync(client);
                }
            }
        }
        catch (OperationCanceledException)
        {
            Log(ProxyConfig.LogLevelEnum.Debug, "Proxy server stopping");
        }
        finally
        {
            combinedCts.Cancel();
        }
    }

    /// <summary>
    /// Stops the proxy server.
    /// </summary>
    public async Task StopAsync()
    {
        _cts?.Cancel();
        _listener.Stop();
        _isListening = false;
        await Task.Delay(100); // Allow pending connections to complete
    }

    /// <summary>
    /// Handles an incoming client connection.
    /// </summary>
    private async Task HandleConnectionAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var buffer = new byte[8192];

                // Read the first request to determine if it's CONNECT or HTTP
                var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0) return;

                var requestLine = Encoding.ASCII.GetString(buffer, 0, bytesRead).Split('\r')[0];
                var parts = requestLine.Split(' ');

                if (parts.Length < 2)
                {
                    Log(ProxyConfig.LogLevelEnum.Error, "Invalid request line");
                    return;
                }

                var method = parts[0];

                if (method.Equals("CONNECT", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleConnectAsync(client, buffer, bytesRead);
                }
                else
                {
                    await HandleHttpRequestAsync(client, method, parts[1], buffer, bytesRead);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not IOException)
            {
                Log(ProxyConfig.LogLevelEnum.Error, $"Connection error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Handles CONNECT requests for HTTPS tunnels.
    /// </summary>
    private async Task HandleConnectAsync(TcpClient client, byte[] buffer, int bytesRead)
    {
        var request = Encoding.ASCII.GetString(buffer, 0, bytesRead);
        var hostPort = request.Split('\r')[0].Split(' ')[1];

        Log(ProxyConfig.LogLevelEnum.Info, $"CONNECT request to {hostPort}");

        // Parse host and port
        var parts = hostPort.Split(':');
        var host = parts[0];
        var port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 443;

        // Get certificate for this host (SNI support via dynamic cert generation)
        var cert = _tlsHandler.GetCertificate(host);

        // Send success response
        await SendResponseAsync(client, "HTTP/1.1 200 Connection Established\r\n\r\n");

        // Switch to TLS mode - encrypt all subsequent traffic
        using (var sslStream = new global::System.Net.Security.SslStream(
            client.GetStream(),
            false,
            ValidateCertificate,
            null))
        {
            await sslStream.AuthenticateAsServerAsync(cert);

            Log(ProxyConfig.LogLevelEnum.Info, $"TLS tunnel established to {host}:{port}");

            // Proxy traffic in both directions
            var proxyTask = ProxyTunnelAsync(sslStream, host, port);
            await Task.WhenAll(proxyTask, Task.CompletedTask);
        }
    }

    /// <summary>
    /// Handles regular HTTP requests (not CONNECT).
    /// The full initial request is already in buffer[0..bytesRead].
    /// </summary>
    private async Task HandleHttpRequestAsync(TcpClient client, string method, string path, byte[] buffer, int bytesRead)
    {
        try
        {
            // Parse the request that was already read by HandleConnectionAsync
            var request = Encoding.ASCII.GetString(buffer, 0, bytesRead);
            var lines = request.Split("\r\n");

            if (lines.Length < 1) return;

            var firstLineParts = lines[0].Split(' ');
            method = firstLineParts[0];
            path = firstLineParts.Length > 1 ? firstLineParts[1] : "/";

            // Extract host and port from the request
            string host;
            int port;
            string relativePath;

            if (path.StartsWith("http://") || path.StartsWith("https://"))
            {
                var uri = new Uri(path);
                host = uri.Host;
                port = uri.IsDefaultPort ? 80 : uri.Port;
                relativePath = uri.PathAndQuery;
            }
            else
            {
                // Look for Host header
                var hostHeader = lines.Skip(1)
                    .FirstOrDefault(l => l.StartsWith("Host:", StringComparison.OrdinalIgnoreCase));

                if (hostHeader == null) return;

                var hostParts = hostHeader.Substring("Host:".Length).Trim().Split(':');
                host = hostParts[0];
                port = hostParts.Length > 1 && int.TryParse(hostParts[1], out var hp) ? hp : 80;
                relativePath = path;
            }

            Log(ProxyConfig.LogLevelEnum.Info, $"{method} {path} to {host}:{port}");

            // Parse headers from the already-read buffer
            var headersDict = new Dictionary<string, string>();
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) break; // End of headers
                var colonIndex = lines[i].IndexOf(':');
                if (colonIndex > 0)
                {
                    var key = lines[i].Substring(0, colonIndex).Trim();
                    var value = lines[i].Substring(colonIndex + 1).Trim();
                    headersDict[key] = value;
                }
            }

            // Parse body from the already-read buffer
            var headerEndIndex = request.IndexOf("\r\n\r\n");
            byte[]? body = null;
            if (headerEndIndex >= 0 && headerEndIndex + 4 < bytesRead)
            {
                body = new byte[bytesRead - (headerEndIndex + 4)];
                Buffer.BlockCopy(buffer, headerEndIndex + 4, body, 0, body.Length);
            }

            // Check if this is a request for the proxy itself
            if (IsRequestForSelf(host, port, path))
            {
                Log(ProxyConfig.LogLevelEnum.Info, "Request directed at proxy itself");
                await HandleSelfRequestAsync(client, method, relativePath);
                return;
            }

            // Intercept request
            var interceptedRequest = new InterceptedRequest
            {
                Method = method,
                Url = new Uri($"http://{host}:{port}{relativePath}"),
                Host = host,
                Port = port,
                Path = relativePath,
                Headers = headersDict,
                Body = body
            };

            var result = await _interceptor.OnRequestAsync(interceptedRequest);
            if (result == null || result.Cancel) return;

            // Forward the request to the target server via a new TCP connection
            await ForwardHttpRequestAsync(client, result, host, port);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not IOException)
        {
            Log(ProxyConfig.LogLevelEnum.Error, $"Request handling error: {ex.Message}");
        }
    }

    /// <summary>
    /// Forwards an intercepted HTTP request to the target server and relays the response back to the client.
    /// Opens a new TCP connection to the target rather than reusing the client connection.
    /// </summary>
    private async Task ForwardHttpRequestAsync(TcpClient client, InterceptedRequest request, string host, int port)
    {
        using var targetClient = new TcpClient();
        await targetClient.ConnectAsync(host, port);
        using var targetStream = targetClient.GetStream();

        // Build the outgoing HTTP request with a relative path
        var outgoing = new StringBuilder();
        outgoing.Append($"{request.Method} {request.Path} HTTP/1.1\r\n");
        outgoing.Append($"Host: {host}\r\n");

        foreach (var header in request.Headers
            .Where(h => !h.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)
                     && !h.Key.Equals("Proxy-Connection", StringComparison.OrdinalIgnoreCase)))
        {
            outgoing.Append($"{header.Key}: {header.Value}\r\n");
        }

        // Add Connection: close so the target closes the connection after the response,
        // which makes it straightforward to detect the end of the response.
        outgoing.Append("Connection: close\r\n");

        if (request.Body != null && request.Body.Length > 0)
            outgoing.Append($"Content-Length: {request.Body.Length}\r\n");

        outgoing.Append("\r\n");

        var requestBytes = Encoding.ASCII.GetBytes(outgoing.ToString());
        await targetStream.WriteAsync(requestBytes, 0, requestBytes.Length);

        if (request.Body != null && request.Body.Length > 0)
            await targetStream.WriteAsync(request.Body, 0, request.Body.Length);

        await targetStream.FlushAsync();

        // Read the full response from the target and relay it back to the client
        var clientStream = client.GetStream();
        var responseBuffer = new byte[8192];
        int read;
        while ((read = await targetStream.ReadAsync(responseBuffer, 0, responseBuffer.Length)) > 0)
        {
            await clientStream.WriteAsync(responseBuffer, 0, read);
        }

        await clientStream.FlushAsync();
    }

    /// <summary>
    /// Proxies traffic between TLS tunnel and target server.
    /// </summary>
    private async Task ProxyTunnelAsync(Stream clientStream, string host, int port)
    {
        using var targetClient = new TcpClient();
        await targetClient.ConnectAsync(host, port);

        using var targetStream = targetClient.GetStream();

        // Bidirectional copy
        var clientToTargetTask = CopyStreamAsync(clientStream, targetStream);
        var targetToClientTask = CopyStreamAsync(targetStream, clientStream);

        await Task.WhenAll(clientToTargetTask, targetToClientTask);
    }

    /// <summary>
    /// Copies data from one stream to another.
    /// </summary>
    private async Task CopyStreamAsync(Stream source, Stream destination)
    {
        var buffer = new byte[8192];
        int bytesRead;

        while ((bytesRead = await source.ReadAsync(buffer)) > 0)
            await destination.WriteAsync(buffer.AsMemory(0, bytesRead));
    }

    /// <summary>
    /// Sends an HTTP response to the client.
    /// </summary>
    private async Task SendResponseAsync(TcpClient client, string response)
    {
        var bytes = Encoding.ASCII.GetBytes(response);
        await client.GetStream().WriteAsync(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Validates the server certificate during TLS handshake.
    /// Accepts all certificates for proxying purposes.
    /// </summary>
    private bool ValidateCertificate(
        object sender,
        System.Security.Cryptography.X509Certificates.X509Certificate certificate,
        System.Security.Cryptography.X509Certificates.X509Chain chain,
        System.Net.Security.SslPolicyErrors sslPolicyErrors)
    {
        // Accept all certificates - this is a proxy that terminates TLS
        return true;
    }

    /// <summary>
    /// Handles requests directed at the proxy itself.
    /// </summary>
    private async Task HandleSelfRequestAsync(TcpClient client, string method, string path)
    {
        _infoPageHandler ??= new InfoPageHandler(_config, _tlsHandler, _startTime, ListeningPort);
        await _infoPageHandler.HandleAsync(client, method, path);
    }

    /// <summary>
    /// Logs a message if the log level permits.
    /// </summary>
    private void Log(ProxyConfig.LogLevelEnum level, string message)
    {
        lock (_lock)
        {
            var timestamp = DateTime.UtcNow.ToString("o");

            switch (level)
            {
                case ProxyConfig.LogLevelEnum.Debug when _config.LogLevel <= ProxyConfig.LogLevelEnum.Debug:
                    Console.WriteLine($"[{timestamp}] DEBUG: {message}");
                    break;
                case ProxyConfig.LogLevelEnum.Info when _config.LogLevel <= ProxyConfig.LogLevelEnum.Info:
                    Console.WriteLine($"[{timestamp}] INFO: {message}");
                    break;
                case ProxyConfig.LogLevelEnum.Warn when _config.LogLevel <= ProxyConfig.LogLevelEnum.Warn:
                    Console.WriteLine($"[{timestamp}] WARN: {message}");
                    break;
                case ProxyConfig.LogLevelEnum.Error when _config.LogLevel <= ProxyConfig.LogLevelEnum.Error:
                    Console.WriteLine($"[{timestamp}] ERROR: {message}");
                    break;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        StopAsync().Wait();
        _infoPageHandler?.Dispose();
        _tlsHandler.Dispose();
        _disposed = true;
    }
}
