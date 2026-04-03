using System.Collections.Concurrent;
using System.Net.Security;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace shmoxy.server;

/// <summary>
/// A pooled upstream connection wrapping a TcpClient and optional SslStream.
/// Consumers use this as the upstream stream, then call ReturnToPool() instead of disposing.
/// </summary>
public sealed class PooledConnection : IDisposable
{
    private readonly ConnectionPool _pool;
    private bool _returned;

    public TcpClient TcpClient { get; }
    public Stream Stream { get; }
    public string Host { get; }
    public int Port { get; }
    public DateTime LastUsed { get; private set; }

    internal PooledConnection(ConnectionPool pool, TcpClient tcpClient, Stream stream, string host, int port)
    {
        _pool = pool;
        TcpClient = tcpClient;
        Stream = stream;
        Host = host;
        Port = port;
        LastUsed = DateTime.UtcNow;
    }

    /// <summary>
    /// Return this connection to the pool for reuse. Must be called instead of Dispose()
    /// when the connection is still healthy.
    /// </summary>
    public void ReturnToPool()
    {
        if (_returned) return;
        _returned = true;
        LastUsed = DateTime.UtcNow;
        _pool.Return(this);
    }

    /// <summary>
    /// Dispose the connection without returning to the pool.
    /// Call this when the connection is unhealthy or no longer needed.
    /// </summary>
    public void Dispose()
    {
        if (_returned) return;
        _returned = true;
        Stream.Dispose();
        TcpClient.Dispose();
    }
}

/// <summary>
/// Simple per-host connection pool for upstream TCP/TLS connections.
/// Keyed by (host, port). Idle connections are evicted after a configurable timeout.
/// </summary>
public sealed class ConnectionPool : IDisposable
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<PooledConnection>> _pools = new();
    private readonly int _maxPerHost;
    private readonly TimeSpan _idleTimeout;
    private readonly RemoteCertificateValidationCallback _certValidator;
    private readonly int _receiveTimeoutMs;
    private readonly ILogger _logger;
    private readonly Timer _evictionTimer;
    private bool _disposed;

    public ConnectionPool(
        int maxPerHost,
        TimeSpan idleTimeout,
        int receiveTimeoutMs,
        RemoteCertificateValidationCallback certValidator,
        ILogger logger)
    {
        _maxPerHost = maxPerHost;
        _idleTimeout = idleTimeout;
        _receiveTimeoutMs = receiveTimeoutMs;
        _certValidator = certValidator;
        _logger = logger;

        // Run eviction every 10 seconds
        _evictionTimer = new Timer(_ => EvictStale(), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    private static string Key(string host, int port) => $"{host}:{port}";

    /// <summary>
    /// Acquire a connection to the given host:port. Returns a pooled connection if available,
    /// otherwise creates a new one.
    /// </summary>
    public async Task<PooledConnection> AcquireAsync(string host, int port, bool useTls)
    {
        var key = Key(host, port);
        var queue = _pools.GetOrAdd(key, _ => new ConcurrentQueue<PooledConnection>());

        // Try to dequeue a healthy connection
        while (queue.TryDequeue(out var conn))
        {
            if (IsHealthy(conn))
            {
                _logger.LogDebug("Reusing pooled connection to {Host}:{Port}", host, port);
                return conn;
            }
            conn.Dispose();
        }

        // Create new connection
        _logger.LogDebug("Creating new connection to {Host}:{Port}", host, port);
        return await CreateConnectionAsync(host, port, useTls);
    }

    /// <summary>
    /// Return a connection to the pool for reuse.
    /// </summary>
    internal void Return(PooledConnection conn)
    {
        if (_disposed)
        {
            conn.Dispose();
            return;
        }

        var key = Key(conn.Host, conn.Port);
        var queue = _pools.GetOrAdd(key, _ => new ConcurrentQueue<PooledConnection>());

        if (queue.Count >= _maxPerHost)
        {
            _logger.LogDebug("Pool full for {Host}:{Port}, disposing connection", conn.Host, conn.Port);
            DisposeConnection(conn);
            return;
        }

        if (!IsHealthy(conn))
        {
            DisposeConnection(conn);
            return;
        }

        queue.Enqueue(conn);
    }

    private async Task<PooledConnection> CreateConnectionAsync(string host, int port, bool useTls)
    {
        var tcpClient = new TcpClient();
        tcpClient.ReceiveTimeout = _receiveTimeoutMs;

        await tcpClient.ConnectAsync(host, port);

        Stream stream = tcpClient.GetStream();
        if (useTls)
        {
            var sslStream = new SslStream(stream, false, _certValidator);
            await sslStream.AuthenticateAsClientAsync(host);
            stream = sslStream;
        }

        return new PooledConnection(this, tcpClient, stream, host, port);
    }

    private bool IsHealthy(PooledConnection conn)
    {
        if (DateTime.UtcNow - conn.LastUsed > _idleTimeout)
            return false;

        try
        {
            var socket = conn.TcpClient.Client;
            // Poll with zero timeout to check if socket is still connected
            return !(socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0);
        }
        catch
        {
            return false;
        }
    }

    private void EvictStale()
    {
        foreach (var kvp in _pools)
        {
            var queue = kvp.Value;
            var count = queue.Count;
            for (var i = 0; i < count; i++)
            {
                if (queue.TryDequeue(out var conn))
                {
                    if (IsHealthy(conn))
                    {
                        queue.Enqueue(conn);
                    }
                    else
                    {
                        DisposeConnection(conn);
                    }
                }
            }
        }
    }

    private static void DisposeConnection(PooledConnection conn)
    {
        try
        {
            conn.Stream.Dispose();
        }
        catch
        {
            // Best-effort cleanup
        }

        try
        {
            conn.TcpClient.Dispose();
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _evictionTimer.Dispose();

        foreach (var kvp in _pools)
        {
            while (kvp.Value.TryDequeue(out var conn))
            {
                DisposeConnection(conn);
            }
        }
    }
}
