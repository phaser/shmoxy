using System.Net.Sockets;
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
    public LogLevel LogLevel { get; set; } = LogLevel.Info;

    /// <summary>
    /// Logging levels.
    /// </summary>
    public enum LogLevel
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

        Log(LogLevel.Info, $"Proxy server initialized on port {config.Port}");
    }

    /// <summary>
    /// Creates a proxy server with custom interceptor.
    /// </summary>
    public ProxyServer(ProxyConfig config, IInterceptHook interceptor) : this(config)
    {
        _interceptor = interceptor ?? throw new ArgumentNullException(nameof(interceptor));
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
            Log(LogLevel.Info, "Proxy server started");

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
            Log(LogLevel.Debug, "Proxy server stopping");
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
                    Log(LogLevel.Error, "Invalid request line");
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
                Log(LogLevel.Error, $"Connection error: {ex.Message}");
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

        Log(LogLevel.Info, $"CONNECT request to {hostPort}");

        // Parse host and port
        var parts = hostPort.Split(':');
        var host = parts[0];
        var port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 443;

        // Get certificate for this host (SNI support via dynamic cert generation)
        var cert = _tlsHandler.GetCertificate(host);

        // Send success response
        await SendResponseAsync(client, "HTTP/1.1 200 Connection Established\r\n\r\n");

        // Switch to TLS mode - encrypt all subsequent traffic
        using (var sslStream = new System.Security.Authentication.SslStream(
            client.GetStream(),
            false,
            ValidateCertificate,
            null))
        {
            await sslStream.AuthenticateAsServerAsync(cert);

            Log(LogLevel.Info, $"TLS tunnel established to {host}:{port}");

            // Proxy traffic in both directions
            var proxyTask = ProxyTunnelAsync(sslStream, host, port);
            await Task.WhenAll(proxyTask, Task.CompletedTask);
        }
    }

    /// <summary>
    /// Handles regular HTTP requests (not CONNECT).
    /// </summary>
    private async Task HandleHttpRequestAsync(TcpClient client, string method, string path, byte[] buffer, int bytesRead)
    {
        try
        {
            // Parse the request to get target host and port
            var request = Encoding.ASCII.GetString(buffer, 0, bytesRead);
            var lines = request.Split('\r\n');

            if (lines.Length < 1) return;

            var firstLineParts = lines[0].Split(' ');
            method = firstLineParts[0];
            path = firstLineParts.Length > 1 ? firstLineParts[1] : "/";

            // Extract host from request or headers
            string host, portStr;

            if (path.StartsWith("http://") || path.StartsWith("https://"))
            {
                var uri = new Uri(path);
                host = uri.Host;
                portStr = uri.IsDefaultPort ? "80" : uri.Port.ToString();
            }
            else
            {
                // Look for Host header
                var hostHeader = lines.Skip(1)
                    .FirstOrDefault(l => l.StartsWith("Host:", StringComparison.OrdinalIgnoreCase));

                if (hostHeader == null) return;

                var hostValue = hostHeader.Split(':')[0].Trim();
                host = hostValue;
                portStr = "80"; // Default for HTTP
            }

            int port = int.TryParse(portStr, out var p) ? p : 80;

            Log(LogLevel.Info, $"{method} {path} to {host}:{port}");

            // Forward request through proxy tunnel
            await ForwardHttpRequestAsync(client, method, path, host, port);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not IOException)
        {
            Log(LogLevel.Error, $"Request handling error: {ex.Message}");
        }
    }

    /// <summary>
    /// Forwards an HTTP request through a proxy tunnel.
    /// </summary>
    private async Task ForwardHttpRequestAsync(TcpClient client, string method, string path, string host, int port)
    {
        using var proxyClient = new ProxyHttpClient(client, host, port, useTunnel: true);

        // Read full request
        var stream = proxyClient.Stream;
        var buffer = new byte[8192];
        var totalRead = 0;
        var headersEnd = false;

        while (!headersEnd)
        {
            var bytesRead = await stream.ReadAsync(buffer, totalRead, buffer.Length - totalRead);
            if (bytesRead == 0) break;

            var chunk = Encoding.ASCII.GetString(buffer, 0, bytesRead);
            if (chunk.Contains("\r\n\r\n")) headersEnd = true;

            totalRead += bytesRead;
        }

        // Parse request to get body and headers for interception
        var requestText = Encoding.ASCII.GetString(buffer, 0, totalRead);
        var headerEndIndex = requestText.IndexOf("\r\n\r\n");

        var headersDict = new Dictionary<string, string>();
        string? bodyStr = null;

        foreach (var line in requestText.Substring(0, Math.Min(headerEndIndex + 1, headerEndIndex)).Split('\n'))
        {
            if (line.Contains(':') && !line.StartsWith("HTTP"))
            {
                var parts = line.Split(':', 2);
                headersDict[parts[0].Trim()] = parts.Length > 1 ? parts[1].Trim() : "";
            }
        }

        // Check for body in first chunk
        if (headerEndIndex >= 0 && headerEndIndex + 4 < totalRead)
        {
            var remainingText = Encoding.ASCII.GetString(buffer, headerEndIndex + 4, totalRead - (headerEndIndex + 4));
            if (!string.IsNullOrEmpty(remainingText))
                bodyStr = remainingText;
        }

        // Intercept request
        var interceptedRequest = new InterceptedRequest
        {
            Method = method,
            Url = new Uri($"http://{host}:{port}{path}"),
            Host = host,
            Port = port,
            Path = path,
            Headers = headersDict,
            Body = bodyStr != null ? Encoding.UTF8.GetBytes(bodyStr) : null
        };

        var result = await _interceptor.OnRequestAsync(interceptedRequest);
        if (result?.Cancel ?? false) return;

        // Send the request through tunnel
        var proxyRequest = new StringBuilder();
        proxyRequest.AppendLine($"{method} {path} HTTP/1.1");
        proxyRequest.AppendLine($"Host: {host}:{port}");

        foreach (var header in result.Headers.Where(h => !h.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase) && !h.Key.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase)))
            proxyRequest.AppendLine($"{header.Key}: {header.Value}");

        if (result.Body != null && result.Body.Length > 0)
            proxyRequest.AppendLine($"Content-Length: {result.Body.Length}");

        proxyRequest.Append("\r\n");

        var requestBytes = Encoding.ASCII.GetBytes(proxyRequest.ToString());
        await stream.WriteAsync(requestBytes, 0, requestBytes.Length);

        if (result.Body != null && result.Body.Length > 0)
            await stream.WriteAsync(result.Body, 0, result.Body.Length);

        // Read and forward response back to client
        var response = await proxyClient.ReadResponseAsync();
        await SendResponseAsync(client, Encoding.ASCII.GetString(response));
    }

    /// <summary>
    /// Proxies traffic between TLS tunnel and target server.
    /// </summary>
    private async Task ProxyTunnelAsync(System.Security.Authentication.SslStream clientStream, string host, int port)
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
    /// Logs a message if the log level permits.
    /// </summary>
    private void Log(ProxyConfig.LogLevel level, string message)
    {
        lock (_lock)
        {
            var timestamp = DateTime.UtcNow.ToString("o");

            switch (level)
            {
                case ProxyConfig.LogLevel.Debug when _config.LogLevel <= ProxyConfig.LogLevel.Debug:
                    Console.WriteLine($"[{timestamp}] DEBUG: {message}");
                    break;
                case ProxyConfig.LogLevel.Info when _config.LogLevel <= ProxyConfig.LogLevel.Info:
                    Console.WriteLine($"[{timestamp}] INFO: {message}");
                    break;
                case ProxyConfig.LogLevel.Warn when _config.LogLevel <= ProxyConfig.LogLevel.Warn:
                    Console.WriteLine($"[{timestamp}] WARN: {message}");
                    break;
                case ProxyConfig.LogLevel.Error when _config.LogLevel <= ProxyConfig.LogLevel.Error:
                    Console.WriteLine($"[{timestamp}] ERROR: {message}");
                    break;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        StopAsync().Wait();
        _tlsHandler.Dispose();
        _disposed = true;
    }
}
