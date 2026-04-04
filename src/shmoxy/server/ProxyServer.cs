using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using shmoxy.models.dto;
using shmoxy.server.helpers;
using shmoxy.server.hooks;
using shmoxy.server.interfaces;
using shmoxy.shared;
using shmoxy.shared.ipc;

namespace shmoxy.server;

/// <summary>
/// Core proxy server that handles HTTP/HTTPS requests with TLS termination.
/// </summary>
public class ProxyServer : IAsyncDisposable, IDisposable
{
    private readonly TcpListener _listener;
    private readonly TlsHandler _tlsHandler;
    private readonly IInterceptHook _interceptor;
    private readonly ProxyConfig _config;
    private readonly ILogger<ProxyServer> _logger;
    private readonly ConcurrentDictionary<Task, byte> _activeConnections = new();
    private readonly ConnectionPool? _connectionPool;
    private CancellationTokenSource? _cts;
    private bool _disposed;
    private volatile bool _isListening;
    private X509Certificate2? _rootCert;

    private const int UpstreamReadTimeoutMs = 30000;

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
    /// Gets the root CA certificate in PEM format.
    /// </summary>
    public string GetRootCertificatePem()
    {
        if (_rootCert == null)
            throw new InvalidOperationException("Root certificate not available");

        return _rootCert.ExportCertificatePem();
    }

    /// <summary>
    /// Gets the root CA certificate in DER format.
    /// </summary>
    public byte[] GetRootCertificateDer()
    {
        if (_rootCert == null)
            throw new InvalidOperationException("Root certificate not available");

        return _rootCert.Export(X509ContentType.Cert);
    }

    /// <summary>
    /// Gets the root CA certificate in PFX format (for internal IPC use only).
    /// </summary>
    public byte[] GetRootCertificatePfx()
    {
        if (_rootCert == null)
            throw new InvalidOperationException("Root certificate not available");

        return _rootCert.Export(X509ContentType.Pfx);
    }

    /// <summary>
    /// Creates a new proxy server with default configuration.
    /// </summary>
    public ProxyServer() : this(new ProxyConfig()) { }

    /// <summary>
    /// Creates a new proxy server with the specified configuration.
    /// </summary>
    public ProxyServer(ProxyConfig config) : this(config, new NoOpInterceptHook(), NullLogger<ProxyServer>.Instance) { }

    /// <summary>
    /// Creates a proxy server with custom interceptor.
    /// </summary>
    public ProxyServer(ProxyConfig config, IInterceptHook interceptor) : this(config, interceptor, NullLogger<ProxyServer>.Instance) { }

    /// <summary>
    /// Creates a proxy server with custom interceptor and logger.
    /// </summary>
    public ProxyServer(ProxyConfig config, IInterceptHook interceptor, ILogger<ProxyServer> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _interceptor = interceptor ?? throw new ArgumentNullException(nameof(interceptor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _listener = TcpListener.Create(config.Port);
        _tlsHandler = new TlsHandler(config.CertStoragePath);
        _rootCert = _tlsHandler.GetRootCertificate();

        if (config.ConnectionPoolSizePerHost > 0)
        {
            _connectionPool = new ConnectionPool(
                config.ConnectionPoolSizePerHost,
                TimeSpan.FromSeconds(config.ConnectionPoolIdleTimeoutSeconds),
                UpstreamReadTimeoutMs,
                ValidateCertificate,
                logger);
            _logger.LogInformation("Connection pool enabled: {PoolSize} per host, {IdleTimeout}s idle timeout",
                config.ConnectionPoolSizePerHost, config.ConnectionPoolIdleTimeoutSeconds);
        }

        _logger.LogInformation("Proxy server initialized on port {Port}", config.Port);
        _logger.LogInformation("Certificate storage: {CertStoragePath}", config.CertStoragePath);
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
            _logger.LogInformation("Proxy server started on port {Port}", _config.Port);

            while (!combinedCts.Token.IsCancellationRequested)
            {
                var clientTask = _listener.AcceptTcpClientAsync();

                await Task.WhenAny(clientTask, Task.Delay(-1, combinedCts.Token));

                if (clientTask.Status == TaskStatus.RanToCompletion && !combinedCts.Token.IsCancellationRequested)
                {
                    var client = await clientTask;
                    var connectionTask = Task.Run(() => HandleConnectionAsync(client));
                    _activeConnections.TryAdd(connectionTask, 0);
                    _ = connectionTask.ContinueWith(t => _activeConnections.TryRemove(t, out _), TaskContinuationOptions.ExecuteSynchronously);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Proxy server stopping");
        }
        catch (SocketException ex)
        {
            _logger.LogError("Failed to bind to port {Port}: {ErrorMessage} (SocketErrorCode: {SocketErrorCode})", _config.Port, ex.Message, ex.SocketErrorCode);
            throw;
        }
        finally
        {
            combinedCts.Cancel();
        }
    }

    /// <summary>
    /// Stops the proxy server.
    /// </summary>
    /// <summary>
    /// Timeout for waiting on active connections to complete during shutdown.
    /// </summary>
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);

    public async Task StopAsync()
    {
        _cts?.Cancel();
        _listener.Stop();
        _isListening = false;

        var activeTasks = _activeConnections.Keys.ToArray();
        if (activeTasks.Length > 0)
        {
            _logger.LogDebug("Waiting for {Count} active connections to complete", activeTasks.Length);
            await Task.WhenAny(Task.WhenAll(activeTasks), Task.Delay(ShutdownTimeout));
        }
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

                // Read until the full HTTP headers are received (\r\n\r\n terminator).
                // A single ReadAsync cannot be relied upon — TCP may deliver data
                // across multiple segments.
                var clientEndpoint = client.Client.RemoteEndPoint?.ToString();
                var headerResult = await ReadUntilHeadersCompleteAsync(stream, _logger, clientEndpoint);
                if (headerResult == null) return;

                var (buffer, bytesRead) = headerResult.Value;

                var requestLine = Encoding.Latin1.GetString(buffer, 0, bytesRead).Split('\r')[0];
                var parts = requestLine.Split(' ');

                if (parts.Length < 2)
                {
                    _logger.LogError("Invalid request line");
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
                _logger.LogError("Connection error: {ErrorMessage}", ex.Message);
            }
        }
    }

    /// <summary>
    /// Handles CONNECT requests for HTTPS tunnels with MITM interception.
    /// If the host matches the passthrough list, traffic is tunneled without TLS termination.
    /// </summary>
    private async Task HandleConnectAsync(TcpClient client, byte[] buffer, int bytesRead)
    {
        var request = Encoding.Latin1.GetString(buffer, 0, bytesRead);
        var hostPort = request.Split('\r')[0].Split(' ')[1];

        _logger.LogInformation("CONNECT request to {HostPort}", hostPort);

        // Parse host and port
        var parts = hostPort.Split(':');
        var host = parts[0];
        var port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 443;

        // Check if host is configured for TLS passthrough
        if (_config.PassthroughHosts.Count > 0 && HostMatcher.IsMatch(host, _config.PassthroughHosts))
        {
            await HandlePassthroughAsync(client, host, port);
            return;
        }

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

            _logger.LogInformation("TLS tunnel established to {Host}:{Port}", host, port);

            // Read decrypted HTTP requests and intercept them
            await HandleTunnelRequestsAsync(sslStream, host, port);
        }
    }

    /// <summary>
    /// Handles a CONNECT request by tunneling raw TCP bytes without TLS termination.
    /// This preserves the client's original TLS fingerprint (JA3/JA4).
    /// </summary>
    private async Task HandlePassthroughAsync(TcpClient client, string host, int port)
    {
        _logger.LogInformation("TLS passthrough for {Host}:{Port}", host, port);

        // Connect to the upstream server
        using var upstream = new TcpClient();
        await upstream.ConnectAsync(host, port);

        // Send 200 to the client so it starts the TLS handshake
        await SendResponseAsync(client, "HTTP/1.1 200 Connection Established\r\n\r\n");

        // Emit a passthrough event for inspection
        await _interceptor.OnPassthroughAsync(host, port);

        // Bidirectional raw TCP relay — no decryption
        var clientStream = client.GetStream();
        var upstreamStream = upstream.GetStream();

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        var clientToUpstream = RelayAsync(clientStream, upstreamStream, cts.Token);
        var upstreamToClient = RelayAsync(upstreamStream, clientStream, cts.Token);

        await Task.WhenAny(clientToUpstream, upstreamToClient);

        // Once one direction completes, cancel the other
        await cts.CancelAsync();

        _logger.LogDebug("TLS passthrough ended for {Host}:{Port}", host, port);
    }

    /// <summary>
    /// Copies data from source to destination until either side closes or cancellation is requested.
    /// </summary>
    private static async Task RelayAsync(NetworkStream source, NetworkStream destination, CancellationToken ct)
    {
        var buffer = new byte[8192];
        try
        {
            int read;
            while ((read = await source.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                await destination.WriteAsync(buffer, 0, read, ct);
                await destination.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (SocketException) { }
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
            var request = Encoding.Latin1.GetString(buffer, 0, bytesRead);
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

                if (hostHeader == null)
                {
                    // For requests without Host header, assume it's to the proxy itself
                    host = "localhost";
                    port = ListeningPort;
                    relativePath = path;
                }
                else
                {
                    var hostParts = hostHeader.Substring("Host:".Length).Trim().Split(':');
                    host = hostParts[0];
                    port = hostParts.Length > 1 && int.TryParse(hostParts[1], out var hp) ? hp : 80;
                    relativePath = path;
                }
            }

            _logger.LogInformation("{Method} {Path} to {Host}:{Port}", method, path, host, port);

            // Serve info page when request is directed to the proxy itself
            if (IsRequestToProxyItself(host, port, path))
            {
                if (path.Equals("/root-ca.pem", StringComparison.OrdinalIgnoreCase))
                {
                    await ServeRootCertificatePemAsync(client);
                    return;
                }
                if (path.Equals("/root-ca.der", StringComparison.OrdinalIgnoreCase))
                {
                    await ServeRootCertificateDerAsync(client);
                    return;
                }
                if (path.Equals("/") || string.IsNullOrEmpty(path))
                {
                    await ServeInfoPageAsync(client, method, path);
                    return;
                }
            }

            // Parse headers from the already-read buffer
            var headersDict = new List<KeyValuePair<string, string>>();
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) break; // End of headers
                var colonIndex = lines[i].IndexOf(':');
                if (colonIndex > 0)
                {
                    var key = lines[i].Substring(0, colonIndex).Trim();
                    var value = lines[i].Substring(colonIndex + 1).Trim();
                    headersDict.Add(new KeyValuePair<string, string>(key, value));
                }
            }

            // Parse body from the already-read buffer, then read remaining if needed
            var headerEndIndex = request.IndexOf("\r\n\r\n");
            byte[]? body = null;
            if (headerEndIndex >= 0 && headerEndIndex + 4 < bytesRead)
            {
                body = new byte[bytesRead - (headerEndIndex + 4)];
                Buffer.BlockCopy(buffer, headerEndIndex + 4, body, 0, body.Length);
            }

            body = await ReadFullBodyAsync(client.GetStream(), body, headersDict);

            // Intercept request
            var interceptedRequest = new InterceptedRequest
            {
                Method = method,
                Url = new Uri($"http://{host}:{port}{relativePath}"),
                Host = host,
                Port = port,
                Path = relativePath,
                Headers = headersDict,
                Body = body,
                CorrelationId = Guid.NewGuid().ToString()
            };

            var result = await _interceptor.OnRequestAsync(interceptedRequest);
            if (result == null || result.Cancel) return;

            // Forward the request to the target server via a new TCP connection
            await ForwardHttpRequestAsync(client, result, host, port);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not IOException)
        {
            _logger.LogError("Request handling error: {ErrorMessage}", ex.Message);
        }
    }

    /// <summary>
    /// Checks if the request is directed to the proxy server itself.
    /// </summary>
    private bool IsRequestToProxyItself(string host, int port, string path)
    {
        var isLocalhost = host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                       || host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
                       || host.Equals("::1", StringComparison.OrdinalIgnoreCase);

        return isLocalhost && port == ListeningPort;
    }

    /// <summary>
    /// Serves the root CA certificate in PEM format.
    /// </summary>
    private async Task ServeRootCertificatePemAsync(TcpClient client)
    {
        try
        {
            var pem = GetRootCertificatePem();
            var response = $"HTTP/1.1 200 OK\r\nContent-Type: application/x-pem-file\r\nContent-Length: {Encoding.UTF8.GetByteCount(pem)}\r\nContent-Disposition: attachment; filename=\"shmoxy-root-ca.pem\"\r\nConnection: close\r\n\r\n{pem}";
            await SendResponseAsync(client, response);
        }
        catch (Exception ex)
        {
            var error = $"HTTP/1.1 500 Internal Server Error\r\nContent-Type: text/plain\r\n\r\nError: {ex.Message}";
            await SendResponseAsync(client, error);
        }
    }

    /// <summary>
    /// Serves the root CA certificate in DER format.
    /// </summary>
    private async Task ServeRootCertificateDerAsync(TcpClient client)
    {
        try
        {
            var der = GetRootCertificateDer();
            var header = $"HTTP/1.1 200 OK\r\nContent-Type: application/x-x509-ca-cert\r\nContent-Length: {der.Length}\r\nContent-Disposition: attachment; filename=\"shmoxy-root-ca.der\"\r\nConnection: close\r\n\r\n";
            var headerBytes = Encoding.Latin1.GetBytes(header);
            var stream = client.GetStream();
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length);
            await stream.WriteAsync(der, 0, der.Length);
            await stream.FlushAsync();
        }
        catch (Exception ex)
        {
            var error = $"HTTP/1.1 500 Internal Server Error\r\nContent-Type: text/plain\r\n\r\nError: {ex.Message}";
            await SendResponseAsync(client, error);
        }
    }

    /// <summary>
    /// Serves an HTML info page for requests directed to the proxy itself.
    /// </summary>
    private async Task ServeInfoPageAsync(TcpClient client, string method, string path)
    {
        if (!method.Equals("GET", StringComparison.OrdinalIgnoreCase))
        {
            await SendResponseAsync(client, "HTTP/1.1 405 Method Not Allowed\r\nContent-Type: text/plain\r\n\r\nMethod Not Allowed");
            return;
        }

        var html = $@"<!DOCTYPE html>
<html>
<head>
    <title>Shmoxy Proxy Server</title>
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; max-width: 800px; margin: 50px auto; padding: 20px; }}
        h1 {{ color: #333; }}
        .status {{ background: #e8f5e9; padding: 15px; border-radius: 5px; margin: 20px 0; }}
        .info {{ background: #e3f2fd; padding: 15px; border-radius: 5px; margin: 20px 0; }}
        .certs {{ background: #fff3e0; padding: 15px; border-radius: 5px; margin: 20px 0; }}
        code {{ background: #f5f5f5; padding: 2px 6px; border-radius: 3px; }}
        a {{ color: #1976d2; }}
        .btn {{ display: inline-block; padding: 8px 16px; background: #1976d2; color: white; text-decoration: none; border-radius: 4px; margin: 5px 5px 5px 0; }}
        .btn:hover {{ background: #1565c0; }}
    </style>
</head>
<body>
    <h1>Shmoxy Proxy Server</h1>
    <div class=""status"">
        <strong>Proxy is running</strong>
    </div>
    <div class=""info"">
        <h2>Server Information</h2>
        <p><strong>Listening on:</strong> <code>http://localhost:{ListeningPort}</code></p>
        <p><strong>Mode:</strong> HTTP/HTTPS Intercepting Proxy</p>
        <p><strong>Status:</strong> Accepting connections</p>
    </div>
    <div class=""certs"">
        <h2>Root CA Certificate</h2>
        <p>Download and install the root CA certificate to trust HTTPS connections through this proxy:</p>
        <p>
            <a href=""/root-ca.pem"" class=""btn"">Download PEM</a>
            <a href=""/root-ca.der"" class=""btn"">Download DER</a>
        </p>
        <h3>Installation Instructions</h3>
        <ul>
            <li><strong>Windows:</strong> Import .DER file into Trusted Root Certification Authorities</li>
            <li><strong>macOS:</strong> Add .PEM to Keychain and set to ""Always Trust""</li>
            <li><strong>Firefox:</strong> Import .PEM in Settings > Privacy & Security > Certificates</li>
            <li><strong>Chrome:</strong> Uses system certificate store</li>
        </ul>
    </div>
    <h2>How to use</h2>
    <p>Configure your browser or application to use this proxy:</p>
    <ul>
        <li>HTTP Proxy: <code>localhost:{ListeningPort}</code></li>
        <li>HTTPS Proxy: <code>localhost:{ListeningPort}</code></li>
    </ul>
    <p>This proxy intercepts HTTP/HTTPS traffic and can be used for debugging, testing, or monitoring network requests.</p>
</body>
</html>";

        var response = $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {Encoding.UTF8.GetByteCount(html)}\r\nConnection: close\r\n\r\n{html}";
        await SendResponseAsync(client, response);
    }

    /// <summary>
    /// Forwards an intercepted HTTP request to the target server and relays the response back to the client.
    /// Opens a new TCP connection to the target rather than reusing the client connection.
    /// </summary>
    private async Task ForwardHttpRequestAsync(TcpClient client, InterceptedRequest request, string host, int port)
    {
        PooledConnection? pooledConn = null;
        TcpClient? ownedClient = null;
        Stream targetStream;

        // --- Timing: connection phase ---
        var sw = Stopwatch.StartNew();
        double? connectMs = null;
        var reused = false;

        if (_connectionPool != null)
        {
            pooledConn = await _connectionPool.AcquireAsync(host, port, useTls: false);
            reused = pooledConn.IsReused;
            if (!reused)
                connectMs = sw.Elapsed.TotalMilliseconds;
            targetStream = pooledConn.Stream;
        }
        else
        {
            ownedClient = new TcpClient();
            ownedClient.ReceiveTimeout = UpstreamReadTimeoutMs;
            await ownedClient.ConnectAsync(host, port);
            connectMs = sw.Elapsed.TotalMilliseconds;
            targetStream = ownedClient.GetStream();
        }

        try
        {
            // Build the outgoing HTTP request with a relative path
            var outgoing = new StringBuilder();
            outgoing.Append($"{request.Method} {request.Path} HTTP/1.1\r\n");
            outgoing.Append($"Host: {host}\r\n");

            foreach (var header in request.Headers
                .Where(h => !h.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)
                         && !h.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase)
                         && !h.Key.Equals("Proxy-Connection", StringComparison.OrdinalIgnoreCase)
                         && !h.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                         && !(_config.DisableCaching && IsCachingHeader(h.Key))))
            {
                outgoing.Append($"{header.Key}: {header.Value}\r\n");
            }

            if (_config.DisableCaching)
                outgoing.Append("Cache-Control: no-cache\r\n");

            var useKeepAlive = pooledConn != null;
            outgoing.Append(useKeepAlive ? "Connection: keep-alive\r\n" : "Connection: close\r\n");

            if (request.Body != null && request.Body.Length > 0)
                outgoing.Append($"Content-Length: {request.Body.Length}\r\n");

            outgoing.Append("\r\n");

            // --- Timing: send phase ---
            var sendStart = sw.Elapsed.TotalMilliseconds;
            var requestBytes = Encoding.Latin1.GetBytes(outgoing.ToString());
            await targetStream.WriteAsync(requestBytes, 0, requestBytes.Length);

            if (request.Body != null && request.Body.Length > 0)
                await targetStream.WriteAsync(request.Body, 0, request.Body.Length);

            await targetStream.FlushAsync();
            var sendMs = sw.Elapsed.TotalMilliseconds - sendStart;

            // Stream the response from the target back to the client
            var clientStream = client.GetStream();
            byte[] responseBytes;
            var success = false;
            double waitMs;
            double receiveMs;

            // --- Timing: wait (TTFB) + receive phases ---
            var responseStart = sw.Elapsed.TotalMilliseconds;

            if (useKeepAlive)
            {
                // ReadFramedHttpResponseAsync reads the entire response; treat as wait+receive combined
                using var readCts = new CancellationTokenSource(UpstreamReadTimeoutMs);
                responseBytes = await ReadFramedHttpResponseAsync(targetStream, clientStream, readCts.Token);
                success = responseBytes.Length > 0;
                waitMs = sw.Elapsed.TotalMilliseconds - responseStart;
                receiveMs = 0;
            }
            else
            {
                using var ms = new MemoryStream();
                var responseBuffer = new byte[8192];
                int read;
                var firstByteReceived = false;
                double firstByteTime = 0;
                using var readCts = new CancellationTokenSource(UpstreamReadTimeoutMs);
                try
                {
                    while ((read = await targetStream.ReadAsync(responseBuffer, 0, responseBuffer.Length, readCts.Token)) > 0)
                    {
                        if (!firstByteReceived)
                        {
                            firstByteTime = sw.Elapsed.TotalMilliseconds;
                            firstByteReceived = true;
                        }
                        await clientStream.WriteAsync(responseBuffer, 0, read);
                        ms.Write(responseBuffer, 0, read);
                        readCts.CancelAfter(UpstreamReadTimeoutMs);
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogDebug("Upstream read timed out for {Host}:{Port}{Path}", host, port, request.Path);
                }
                catch (IOException)
                {
                    // Connection closed by server — expected with Connection: close
                }
                await clientStream.FlushAsync();
                responseBytes = ms.ToArray();

                if (firstByteReceived)
                {
                    waitMs = firstByteTime - responseStart;
                    receiveMs = sw.Elapsed.TotalMilliseconds - firstByteTime;
                }
                else
                {
                    waitMs = sw.Elapsed.TotalMilliseconds - responseStart;
                    receiveMs = 0;
                }
            }

            // Build timing info (no TLS for plain HTTP)
            var timing = new TimingInfo
            {
                ConnectMs = connectMs,
                TlsMs = null,
                SendMs = sendMs,
                WaitMs = waitMs,
                ReceiveMs = receiveMs,
                Reused = reused
            };

            // Parse and capture response for inspection
            if (responseBytes.Length > 0)
            {
                var (respStatusCode, respHeaders, respBody) = ParseRawHttpResponse(responseBytes);

                // Decompress body for inspection hooks so they see readable content.
                // The client already received the original compressed bytes above.
                var inspectionBody = DecompressForInspection(respBody, respHeaders, _logger);
                var inspectionHeaders = new List<KeyValuePair<string, string>>(respHeaders);
                if (inspectionBody != respBody)
                    inspectionHeaders.RemoveAll(h => h.Key.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase));

                var interceptedResponse = new InterceptedResponse
                {
                    StatusCode = respStatusCode,
                    Headers = inspectionHeaders,
                    Body = inspectionBody,
                    CorrelationId = request.CorrelationId,
                    Timing = timing
                };
                await _interceptor.OnResponseAsync(interceptedResponse);
            }

            // Return healthy pooled connection for reuse, or dispose
            if (pooledConn != null)
            {
                if (success && IsKeepAliveResponse(responseBytes))
                    pooledConn.ReturnToPool();
                else
                    pooledConn.Dispose();
                pooledConn = null;
            }
        }
        finally
        {
            pooledConn?.Dispose();
            ownedClient?.Dispose();
        }
    }

    /// <summary>
    /// Reads decrypted HTTP requests from the MITM tunnel, runs them through
    /// the intercept hook, forwards to the upstream server, and relays the response.
    /// </summary>
    private async Task HandleTunnelRequestsAsync(Stream clientStream, string host, int port)
    {
        // Handle multiple requests on the same connection (HTTP keep-alive)
        while (true)
        {
            // Read until the full HTTP headers are received (\r\n\r\n terminator).
            (byte[] buf, int read)? headerResult;
            try
            {
                headerResult = await ReadUntilHeadersCompleteAsync(clientStream, _logger, $"{host}:{port}");
            }
            catch (IOException)
            {
                break;
            }
            if (headerResult == null) break;

            var (buf, read) = headerResult.Value;

            var requestText = Encoding.Latin1.GetString(buf, 0, read);
            var lines = requestText.Split("\r\n");
            if (lines.Length == 0) break;

            var firstLineParts = lines[0].Split(' ');
            if (firstLineParts.Length < 2) break;

            var method = firstLineParts[0];
            var path = firstLineParts[1];

            // Parse headers
            var headers = new List<KeyValuePair<string, string>>();
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) break;
                var colonIndex = lines[i].IndexOf(':');
                if (colonIndex > 0)
                {
                    var key = lines[i][..colonIndex].Trim();
                    var value = lines[i][(colonIndex + 1)..].Trim();
                    headers.Add(new KeyValuePair<string, string>(key, value));
                }
            }

            // Parse body from the already-read buffer, then read remaining if needed
            var headerEnd = requestText.IndexOf("\r\n\r\n");
            byte[]? body = null;
            if (headerEnd >= 0 && headerEnd + 4 < read)
            {
                body = new byte[read - (headerEnd + 4)];
                Buffer.BlockCopy(buf, headerEnd + 4, body, 0, body.Length);
            }

            body = await ReadFullBodyAsync(clientStream, body, headers);

            var scheme = port == 443 ? "https" : "http";

            _logger.LogInformation("MITM {Method} {Scheme}://{Host}{Path}", method, scheme, host, path);

            // Intercept request
            var correlationId = Guid.NewGuid().ToString();
            var interceptedRequest = new InterceptedRequest
            {
                Method = method,
                Url = new Uri($"{scheme}://{host}{path}"),
                Host = host,
                Port = port,
                Path = path,
                Headers = headers,
                Body = body,
                CorrelationId = correlationId
            };

            var result = await _interceptor.OnRequestAsync(interceptedRequest);
            if (result == null || result.Cancel) break;

            // Forward to upstream — activate temporary passthrough directly if the connection fails.
            PooledConnection? pooledConn = null;
            TcpClient? ownedTargetClient = null;
            try
            {
                // --- Timing: connection phase ---
                var sw = Stopwatch.StartNew();
                double? connectMs = null;
                double? tlsMs = null;
                var reused = false;

                Stream targetStream;
                if (_connectionPool != null)
                {
                    pooledConn = await _connectionPool.AcquireAsync(host, port, useTls: port == 443);
                    reused = pooledConn.IsReused;
                    if (!reused)
                        connectMs = sw.Elapsed.TotalMilliseconds;
                    targetStream = pooledConn.Stream;
                }
                else
                {
                    ownedTargetClient = new TcpClient();
                    ownedTargetClient.ReceiveTimeout = UpstreamReadTimeoutMs;
                    try
                    {
                        await ownedTargetClient.ConnectAsync(host, port);
                    }
                    catch (Exception connectEx)
                    {
                        _logger.LogWarning(
                            "TCP connect failed for {Host}:{Port}: {ExceptionType}: {ErrorMessage}", host, port, connectEx.GetType().Name, connectEx.Message);
                        throw;
                    }
                    connectMs = sw.Elapsed.TotalMilliseconds;

                    if (port == 443)
                    {
                        var tlsStart = sw.Elapsed.TotalMilliseconds;
                        var sslTarget = new global::System.Net.Security.SslStream(
                            ownedTargetClient.GetStream(),
                            false,
                            ValidateCertificate);
                        try
                        {
                            await sslTarget.AuthenticateAsClientAsync(host);
                        }
                        catch (Exception tlsEx)
                        {
                            _logger.LogWarning(
                                "TLS handshake failed for {Host}:{Port}: {ExceptionType}: {ErrorMessage}", host, port, tlsEx.GetType().Name, tlsEx.Message);
                            throw;
                        }
                        tlsMs = sw.Elapsed.TotalMilliseconds - tlsStart;
                        targetStream = sslTarget;
                    }
                    else
                    {
                        targetStream = ownedTargetClient.GetStream();
                    }
                }

                var useKeepAlive = pooledConn != null;

                try
                {
                    // Build outgoing request
                    var outgoing = new StringBuilder();
                    outgoing.Append($"{result.Method} {result.Path} HTTP/1.1\r\n");
                    outgoing.Append($"Host: {host}\r\n");

                    foreach (var header in result.Headers
                        .Where(h => !h.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)
                                 && !h.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase)
                                 && !h.Key.Equals("Proxy-Connection", StringComparison.OrdinalIgnoreCase)
                                 && !h.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                                 && !(_config.DisableCaching && IsCachingHeader(h.Key))))
                    {
                        outgoing.Append($"{header.Key}: {header.Value}\r\n");
                    }

                    if (_config.DisableCaching)
                        outgoing.Append("Cache-Control: no-cache\r\n");

                    outgoing.Append(useKeepAlive ? "Connection: keep-alive\r\n" : "Connection: close\r\n");

                    if (result.Body != null && result.Body.Length > 0)
                        outgoing.Append($"Content-Length: {result.Body.Length}\r\n");

                    outgoing.Append("\r\n");

                    // --- Timing: send phase ---
                    var sendStart = sw.Elapsed.TotalMilliseconds;
                    var requestBytes = Encoding.Latin1.GetBytes(outgoing.ToString());
                    await targetStream.WriteAsync(requestBytes, 0, requestBytes.Length);

                    if (result.Body != null && result.Body.Length > 0)
                        await targetStream.WriteAsync(result.Body, 0, result.Body.Length);

                    await targetStream.FlushAsync();
                    var sendMs = sw.Elapsed.TotalMilliseconds - sendStart;

                    // Stream the response back to the client
                    byte[] responseBytes;
                    var success = false;
                    double waitMs;
                    double receiveMs;

                    // --- Timing: wait (TTFB) + receive phases ---
                    var responseStart = sw.Elapsed.TotalMilliseconds;

                    if (useKeepAlive)
                    {
                        using var readCts = new CancellationTokenSource(UpstreamReadTimeoutMs);
                        responseBytes = await ReadFramedHttpResponseAsync(targetStream, clientStream, readCts.Token);
                        success = responseBytes.Length > 0;
                        waitMs = sw.Elapsed.TotalMilliseconds - responseStart;
                        receiveMs = 0;
                    }
                    else
                    {
                        using var ms = new MemoryStream();
                        var responseBuf = new byte[8192];
                        int responseRead;
                        var firstByteReceived = false;
                        double firstByteTime = 0;
                        using var readCts = new CancellationTokenSource(UpstreamReadTimeoutMs);
                        try
                        {
                            while ((responseRead = await targetStream.ReadAsync(responseBuf, 0, responseBuf.Length, readCts.Token)) > 0)
                            {
                                if (!firstByteReceived)
                                {
                                    firstByteTime = sw.Elapsed.TotalMilliseconds;
                                    firstByteReceived = true;
                                }
                                await clientStream.WriteAsync(responseBuf, 0, responseRead);
                                ms.Write(responseBuf, 0, responseRead);
                                readCts.CancelAfter(UpstreamReadTimeoutMs);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            _logger.LogDebug("Upstream read timed out for {Host}:{Port}{Path}", host, port, result.Path);
                        }
                        catch (IOException)
                        {
                            // Connection closed by server — expected with Connection: close
                        }
                        await clientStream.FlushAsync();
                        responseBytes = ms.ToArray();

                        if (firstByteReceived)
                        {
                            waitMs = firstByteTime - responseStart;
                            receiveMs = sw.Elapsed.TotalMilliseconds - firstByteTime;
                        }
                        else
                        {
                            waitMs = sw.Elapsed.TotalMilliseconds - responseStart;
                            receiveMs = 0;
                        }
                    }

                    var timing = new TimingInfo
                    {
                        ConnectMs = connectMs,
                        TlsMs = tlsMs,
                        SendMs = sendMs,
                        WaitMs = waitMs,
                        ReceiveMs = receiveMs,
                        Reused = reused
                    };

                    // Parse response for intercept hook
                    if (responseBytes.Length > 0)
                    {
                        var (respStatusCode, respHeaders, respBody) = ParseRawHttpResponse(responseBytes);

                        // Decompress body for inspection hooks so they see readable content.
                        // The client already received the original compressed bytes above.
                        var inspectionBody = DecompressForInspection(respBody, respHeaders, _logger);
                        var inspectionHeaders = new List<KeyValuePair<string, string>>(respHeaders);
                        if (inspectionBody != respBody)
                            inspectionHeaders.RemoveAll(h => h.Key.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase));

                        var interceptedResponse = new InterceptedResponse
                        {
                            StatusCode = respStatusCode,
                            Headers = inspectionHeaders,
                            Body = inspectionBody,
                            CorrelationId = correlationId,
                            Timing = timing
                        };
                        await _interceptor.OnResponseAsync(interceptedResponse);

                        // WebSocket upgrade: switch to frame relay
                        if (respStatusCode == 101 && IsWebSocketUpgrade(respHeaders))
                        {
                            var useDeflate = WebSocketDeflateDecompressor.IsDeflateNegotiated(respHeaders);
                            _logger.LogInformation("WebSocket upgrade for {Host}:{Port}{Path} (deflate={Deflate})", host, port, result.Path, useDeflate);
                            await HandleWebSocketRelayAsync(clientStream, targetStream, host, port, result.Path, correlationId, useDeflate);
                            return;
                        }
                    }

                    // Return healthy pooled connection for reuse, or dispose
                    if (pooledConn != null)
                    {
                        if (success && IsKeepAliveResponse(responseBytes))
                            pooledConn.ReturnToPool();
                        else
                            pooledConn.Dispose();
                        pooledConn = null;
                    }
                }
                finally
                {
                    pooledConn?.Dispose();
                    if (ownedTargetClient != null)
                    {
                        targetStream.Dispose();
                        ownedTargetClient.Dispose();
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException
                or AuthenticationException)
            {
                var inner = ex.InnerException != null ? $" Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}" : "";
                _logger.LogWarning(
                    "Upstream connection failed for {Host}:{Port}{Path}: {ExceptionType}: {ErrorMessage}{InnerException}", host, port, result.Path, ex.GetType().Name, ex.Message, inner);
            }

            // Without pool, Connection: close means we're done after one request
            if (_connectionPool == null) break;
        }
    }

    /// <summary>
    /// Sends an HTTP response to the client.
    /// </summary>
    private async Task SendResponseAsync(TcpClient client, string response)
    {
        var bytes = Encoding.UTF8.GetBytes(response);
        var stream = client.GetStream();
        await stream.WriteAsync(bytes, 0, bytes.Length);
        await stream.FlushAsync();
    }

    /// <summary>
    /// Maximum allowed size for HTTP request headers (64 KB).
    /// Requests with headers exceeding this limit are rejected.
    /// </summary>
    private const int MaxHeaderSize = 65536;

    /// <summary>
    /// Reads from the stream until the complete HTTP headers are received
    /// (i.e., the \r\n\r\n terminator is found). TCP is a stream protocol and
    /// a single ReadAsync call may not return the full headers.
    /// Returns (buffer, totalBytesRead) or null if the stream closed before headers completed.
    /// </summary>
    internal static async Task<(byte[] Buffer, int BytesRead)?> ReadUntilHeadersCompleteAsync(
        Stream stream, ILogger? logger = null, string? clientEndpoint = null)
    {
        var buffer = new byte[8192];
        var totalRead = 0;

        while (true)
        {
            if (totalRead == buffer.Length)
            {
                if (buffer.Length >= MaxHeaderSize)
                {
                    logger?.LogWarning(
                        "Client {Client}: request headers exceeded {MaxBytes} bytes limit, closing connection",
                        clientEndpoint ?? "unknown", MaxHeaderSize);
                    return null; // Headers too large
                }

                var newBuffer = new byte[Math.Min(buffer.Length * 2, MaxHeaderSize)];
                Buffer.BlockCopy(buffer, 0, newBuffer, 0, totalRead);
                buffer = newBuffer;
            }

            var bytesRead = await stream.ReadAsync(buffer, totalRead, buffer.Length - totalRead);
            if (bytesRead == 0)
            {
                // Stream closed — return what we have if anything was read
                return totalRead > 0 ? (buffer, totalRead) : null;
            }

            totalRead += bytesRead;

            // Check if the header terminator \r\n\r\n exists in what we've read so far.
            // Only need to search the region that could span the boundary of the new data.
            var searchStart = Math.Max(0, totalRead - bytesRead - 3);
            if (FindHeaderTerminator(buffer, searchStart, totalRead) >= 0)
                return (buffer, totalRead);
        }
    }

    /// <summary>
    /// Searches for the \r\n\r\n header terminator in a byte buffer.
    /// Returns the index of the first \r in the sequence, or -1 if not found.
    /// </summary>
    private static int FindHeaderTerminator(byte[] buffer, int start, int end)
    {
        for (var i = start; i <= end - 4; i++)
        {
            if (buffer[i] == (byte)'\r' && buffer[i + 1] == (byte)'\n' &&
                buffer[i + 2] == (byte)'\r' && buffer[i + 3] == (byte)'\n')
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Reads the full request body from the stream when the initial buffer read was incomplete.
    /// Checks Content-Length to determine if more bytes need to be read beyond what was
    /// already captured in the initial 8KB buffer.
    /// </summary>
    private static async Task<byte[]?> ReadFullBodyAsync(Stream stream, byte[]? initialBody, List<KeyValuePair<string, string>> headers)
    {
        if (!headers.TryGetHeaderValue("Content-Length", out var clHeader) ||
            !int.TryParse(clHeader, out var contentLength) ||
            contentLength <= 0)
        {
            return initialBody;
        }

        var alreadyRead = initialBody?.Length ?? 0;
        if (alreadyRead >= contentLength)
            return initialBody;

        // Need to read remaining bytes
        var fullBody = new byte[contentLength];
        if (initialBody != null)
            Buffer.BlockCopy(initialBody, 0, fullBody, 0, alreadyRead);

        var remaining = contentLength - alreadyRead;
        var offset = alreadyRead;
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(fullBody, offset, remaining);
            if (read == 0)
                break;

            offset += read;
            remaining -= read;
        }

        return fullBody;
    }

    /// <summary>
    /// Reads a complete HTTP response from an upstream stream using proper framing
    /// (Content-Length or chunked Transfer-Encoding). Streams bytes to the client as
    /// they arrive. Returns all raw bytes for inspection parsing.
    /// </summary>
    private static async Task<byte[]> ReadFramedHttpResponseAsync(
        Stream targetStream, Stream clientStream, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[8192];

        // Phase 1: Read until we have the complete header section (\r\n\r\n)
        var headerEndByteIndex = -1;
        while (headerEndByteIndex < 0)
        {
            int read;
            try
            {
                read = await targetStream.ReadAsync(buffer, 0, buffer.Length, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (IOException) { break; }

            if (read == 0) break;
            await clientStream.WriteAsync(buffer, 0, read, ct);
            ms.Write(buffer, 0, read);

            // Search for \r\n\r\n in accumulated bytes
            var accumulated = ms.ToArray();
            headerEndByteIndex = FindHeaderEndIndex(accumulated);
        }

        if (headerEndByteIndex < 0)
            return ms.ToArray();

        // Phase 2: Parse headers to determine body framing
        var allBytes = ms.ToArray();
        var headerStr = Encoding.Latin1.GetString(allBytes, 0, headerEndByteIndex);
        var bodyStartIndex = headerEndByteIndex + 4;
        var bodyBytesAlreadyRead = allBytes.Length - bodyStartIndex;

        var contentLength = ParseContentLengthFromHeaders(headerStr);
        var isChunked = headerStr.Contains("Transfer-Encoding: chunked", StringComparison.OrdinalIgnoreCase)
                     || headerStr.Contains("transfer-encoding: chunked", StringComparison.OrdinalIgnoreCase);

        if (contentLength >= 0)
        {
            // Read exact body length
            var remaining = contentLength - bodyBytesAlreadyRead;
            while (remaining > 0)
            {
                var toRead = (int)Math.Min(remaining, buffer.Length);
                int read;
                try
                {
                    read = await targetStream.ReadAsync(buffer, 0, toRead, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (IOException) { break; }
                if (read == 0) break;
                await clientStream.WriteAsync(buffer, 0, read, ct);
                ms.Write(buffer, 0, read);
                remaining -= read;
            }
        }
        else if (isChunked)
        {
            // Read until the chunk terminator "0\r\n\r\n"
            while (true)
            {
                var data = ms.ToArray();
                if (EndsWithChunkTerminator(data, bodyStartIndex))
                    break;

                int read;
                try
                {
                    read = await targetStream.ReadAsync(buffer, 0, buffer.Length, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (IOException) { break; }
                if (read == 0) break;
                await clientStream.WriteAsync(buffer, 0, read, ct);
                ms.Write(buffer, 0, read);
            }
        }
        else
        {
            // No Content-Length and not chunked — read until connection closes
            while (true)
            {
                int read;
                try
                {
                    read = await targetStream.ReadAsync(buffer, 0, buffer.Length, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (IOException) { break; }
                if (read == 0) break;
                await clientStream.WriteAsync(buffer, 0, read, ct);
                ms.Write(buffer, 0, read);
            }
        }

        await clientStream.FlushAsync(ct);
        return ms.ToArray();
    }

    internal static int FindHeaderEndIndex(byte[] data)
    {
        // Search for \r\n\r\n (0x0D 0x0A 0x0D 0x0A)
        for (var i = 0; i <= data.Length - 4; i++)
        {
            if (data[i] == 0x0D && data[i + 1] == 0x0A && data[i + 2] == 0x0D && data[i + 3] == 0x0A)
                return i;
        }
        return -1;
    }

    internal static long ParseContentLengthFromHeaders(string headerStr)
    {
        foreach (var line in headerStr.Split("\r\n"))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                var val = line["Content-Length:".Length..].Trim();
                if (long.TryParse(val, out var len))
                    return len;
            }
        }
        return -1;
    }

    internal static bool EndsWithChunkTerminator(byte[] data, int bodyStart)
    {
        // Look for "0\r\n\r\n" at the end of the body section
        if (data.Length - bodyStart < 5) return false;
        var end = data.Length;
        return data[end - 5] == '0'
            && data[end - 4] == '\r'
            && data[end - 3] == '\n'
            && data[end - 2] == '\r'
            && data[end - 1] == '\n';
    }

    /// <summary>
    /// Checks whether the given header name is a caching-related header that should be
    /// stripped when the DisableCaching option is enabled.
    /// </summary>
    internal static bool IsCachingHeader(string headerName) =>
        headerName.Equals("If-Modified-Since", StringComparison.OrdinalIgnoreCase)
        || headerName.Equals("If-None-Match", StringComparison.OrdinalIgnoreCase)
        || headerName.Equals("Cache-Control", StringComparison.OrdinalIgnoreCase)
        || headerName.Equals("If-Match", StringComparison.OrdinalIgnoreCase)
        || headerName.Equals("If-Unmodified-Since", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Checks whether the response indicates the server will keep the connection open.
    /// HTTP/1.1 defaults to keep-alive unless "Connection: close" is present.
    /// </summary>
    internal static bool IsKeepAliveResponse(byte[] responseBytes)
    {
        var headerEndIndex = FindHeaderEndIndex(responseBytes);
        if (headerEndIndex < 0) return false;
        var headerStr = Encoding.Latin1.GetString(responseBytes, 0, headerEndIndex);
        // Check for explicit Connection: close
        foreach (var line in headerStr.Split("\r\n"))
        {
            if (line.StartsWith("Connection:", StringComparison.OrdinalIgnoreCase) &&
                line.Contains("close", StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Parses a raw HTTP response into status code, headers, and body.
    /// </summary>
    internal static (int StatusCode, List<KeyValuePair<string, string>> Headers, byte[] Body) ParseRawHttpResponse(byte[] rawResponse)
    {
        var headerText = Encoding.Latin1.GetString(rawResponse, 0, Math.Min(rawResponse.Length, 8192));

        var headerEndIndex = headerText.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (headerEndIndex < 0)
        {
            // No header/body separator found — treat entire response as headers-only
            var lines = headerText.Split("\r\n");
            var statusLine = lines.FirstOrDefault() ?? "";
            var statusParts = statusLine.Split(' ');
            var sc = statusParts.Length >= 2 && int.TryParse(statusParts[1], out var s) ? s : 0;
            return (sc, ParseHeaders(lines.Skip(1)), Array.Empty<byte>());
        }

        var headerSection = headerText[..headerEndIndex];
        var headerLines = headerSection.Split("\r\n");

        // Parse status code from first line
        var firstLine = headerLines.FirstOrDefault() ?? "";
        var parts = firstLine.Split(' ');
        var statusCode = parts.Length >= 2 && int.TryParse(parts[1], out var code) ? code : 0;

        // Parse headers from remaining lines
        var headers = ParseHeaders(headerLines.Skip(1));

        // Body starts after \r\n\r\n (4 bytes past the header end in the raw bytes)
        var bodyStart = headerEndIndex + 4;
        byte[] body;
        if (bodyStart < rawResponse.Length)
        {
            body = new byte[rawResponse.Length - bodyStart];
            Buffer.BlockCopy(rawResponse, bodyStart, body, 0, body.Length);
        }
        else
        {
            body = Array.Empty<byte>();
        }

        // Decode chunked transfer encoding if present
        if (headers.TryGetHeaderValue("Transfer-Encoding", out var te) &&
            te.Contains("chunked", StringComparison.OrdinalIgnoreCase) &&
            body.Length > 0)
        {
            body = DecodeChunkedBody(body);
        }

        return (statusCode, headers, body);
    }

    /// <summary>
    /// Decompresses a response body for inspection hooks based on Content-Encoding.
    /// Returns the original body unchanged if no encoding is present or decompression fails.
    /// </summary>
    internal static byte[] DecompressForInspection(byte[] body, List<KeyValuePair<string, string>> headers, ILogger logger)
    {
        if (body.Length == 0)
            return body;

        if (!headers.TryGetHeaderValue("Content-Encoding", out var encoding))
            return body;

        var enc = encoding.Trim().ToLowerInvariant();
        try
        {
            using var input = new MemoryStream(body);
            using var output = new MemoryStream();
            Stream? decompressionStream = enc switch
            {
                "gzip" => new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress),
                "deflate" => new System.IO.Compression.DeflateStream(input, System.IO.Compression.CompressionMode.Decompress),
                "br" => new System.IO.Compression.BrotliStream(input, System.IO.Compression.CompressionMode.Decompress),
                _ => null
            };

            if (decompressionStream == null)
                return body;

            using (decompressionStream)
            {
                decompressionStream.CopyTo(output);
            }

            return output.ToArray();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to decompress {Encoding} response body for inspection, passing raw bytes", enc);
            return body;
        }
    }

    /// <summary>
    /// Decodes a chunked transfer-encoded body by stripping chunk-size lines and the terminating chunk.
    /// </summary>
    internal static byte[] DecodeChunkedBody(byte[] chunkedData)
    {
        using var output = new MemoryStream();
        var offset = 0;

        while (offset < chunkedData.Length)
        {
            // Find the end of the chunk-size line (\r\n)
            var lineEnd = FindCrLf(chunkedData, offset);
            if (lineEnd < 0)
                break;

            // Parse chunk size (hex)
            var sizeLine = Encoding.Latin1.GetString(chunkedData, offset, lineEnd - offset).Trim();
            // Chunk extensions (after ';') are allowed by RFC but rare — strip them
            var semiColon = sizeLine.IndexOf(';');
            if (semiColon >= 0)
                sizeLine = sizeLine[..semiColon].Trim();

            if (!int.TryParse(sizeLine, System.Globalization.NumberStyles.HexNumber, null, out var chunkSize) ||
                chunkSize == 0)
                break;

            // Move past the \r\n after the size line
            var dataStart = lineEnd + 2;
            if (dataStart + chunkSize > chunkedData.Length)
                break;

            output.Write(chunkedData, dataStart, chunkSize);

            // Skip past chunk data + trailing \r\n
            offset = dataStart + chunkSize + 2;
        }

        return output.ToArray();
    }

    private static int FindCrLf(byte[] data, int offset)
    {
        for (var i = offset; i < data.Length - 1; i++)
        {
            if (data[i] == (byte)'\r' && data[i + 1] == (byte)'\n')
                return i;
        }
        return -1;
    }

    private static List<KeyValuePair<string, string>> ParseHeaders(IEnumerable<string> lines)
    {
        var headers = new List<KeyValuePair<string, string>>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) break;
            var colonIndex = line.IndexOf(':');
            if (colonIndex > 0)
            {
                var key = line[..colonIndex].Trim();
                var value = line[(colonIndex + 1)..].Trim();
                headers.Add(new KeyValuePair<string, string>(key, value));
            }
        }
        return headers;
    }

    /// <summary>
    /// Validates the server certificate during TLS handshake.
    /// When <see cref="ProxyConfig.ValidateUpstreamCertificates"/> is disabled, accepts all
    /// certificates. When enabled, rejects certificates that fail system trust validation.
    /// </summary>
    internal bool ValidateCertificate(
        object sender,
        System.Security.Cryptography.X509Certificates.X509Certificate? certificate,
        System.Security.Cryptography.X509Certificates.X509Chain? chain,
        System.Net.Security.SslPolicyErrors sslPolicyErrors)
    {
        if (!_config.ValidateUpstreamCertificates)
            return true;

        if (sslPolicyErrors == System.Net.Security.SslPolicyErrors.None)
            return true;

        var subject = certificate?.Subject ?? "unknown";
        var issuer = certificate?.Issuer ?? "unknown";
        _logger.LogWarning(
            "Upstream certificate validation failed. Subject: {Subject}, Issuer: {Issuer}, Errors: {SslPolicyErrors}",
            subject, issuer, sslPolicyErrors);
        return false;
    }

    internal static bool IsWebSocketUpgrade(List<KeyValuePair<string, string>> headers)
    {
        var hasUpgrade = headers.Any(h =>
            h.Key.Equals("Upgrade", StringComparison.OrdinalIgnoreCase) &&
            h.Value.Contains("websocket", StringComparison.OrdinalIgnoreCase));
        var hasConnection = headers.Any(h =>
            h.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase) &&
            h.Value.Contains("Upgrade", StringComparison.OrdinalIgnoreCase));
        return hasUpgrade && hasConnection;
    }

    private async Task HandleWebSocketRelayAsync(Stream clientStream, Stream serverStream, string host, int port, string path, string correlationId, bool useDeflate)
    {
        await _interceptor.OnWebSocketOpenAsync(host, path, correlationId);

        using var cts = new CancellationTokenSource();

        var clientToServer = RelayWebSocketFramesAsync(clientStream, serverStream, "client", host, port, correlationId, cts, useDeflate);
        var serverToClient = RelayWebSocketFramesAsync(serverStream, clientStream, "server", host, port, correlationId, cts, useDeflate);

        await Task.WhenAny(clientToServer, serverToClient);
        await cts.CancelAsync();

        await _interceptor.OnWebSocketCloseAsync(correlationId, null);
        _logger.LogDebug("WebSocket relay ended for {Host}:{Port}", host, port);
    }

    private async Task RelayWebSocketFramesAsync(
        Stream source, Stream destination, string direction, string host, int port,
        string correlationId, CancellationTokenSource cts, bool useDeflate)
    {
        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var frame = await WebSocketFrameReader.ReadFrameAsync(source, cts.Token);
                if (frame == null)
                    break;

                // Decompress for inspection if this is a compressed data frame
                var inspectionFrame = frame;
                if (useDeflate && frame.Rsv1 && frame.Payload.Length > 0)
                {
                    var decompressed = WebSocketDeflateDecompressor.TryDecompress(frame.Payload);
                    if (decompressed != null)
                    {
                        inspectionFrame = frame with { Payload = decompressed, Rsv1 = false };
                    }
                }

                await _interceptor.OnWebSocketFrameAsync(correlationId, inspectionFrame, direction);

                // Forward ORIGINAL compressed frame to preserve protocol correctness
                var maskFrame = direction == "client";
                await WebSocketFrameReader.WriteFrameAsync(destination, frame, cts.Token, mask: maskFrame);
                await destination.FlushAsync(cts.Token);

                if (frame.Opcode == shmoxy.models.WebSocketOpcode.Close)
                {
                    _logger.LogDebug("WebSocket close from {Direction} for {Host}:{Port}", direction, host, port);
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        await StopAsync();
        _tlsHandler.Dispose();
        _disposed = true;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _cts?.Cancel();
        _listener.Stop();
        _isListening = false;
        _connectionPool?.Dispose();
        _tlsHandler.Dispose();
        _disposed = true;
    }
}
