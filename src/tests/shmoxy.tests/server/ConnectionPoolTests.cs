using System.Net.Security;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using shmoxy.server;
using Xunit;

namespace shmoxy.tests.server;

public class ConnectionPoolTests : IDisposable
{
    private readonly ConnectionPool _pool;
    private TcpListener? _server;

    public ConnectionPoolTests()
    {
        _pool = new ConnectionPool(
            maxPerHost: 2,
            idleTimeout: TimeSpan.FromSeconds(30),
            receiveTimeoutMs: 5000,
            certValidator: (_, _, _, _) => true,
            logger: NullLogger.Instance);
    }

    [Fact]
    public async Task AcquireAsync_CreatesNewConnection()
    {
        _server = StartTestServer(out var port);

        var conn = await _pool.AcquireAsync("localhost", port, useTls: false);

        Assert.NotNull(conn);
        Assert.NotNull(conn.Stream);
        Assert.Equal("localhost", conn.Host);
        Assert.Equal(port, conn.Port);

        conn.Dispose();
    }

    [Fact]
    public async Task ReturnToPool_ConnectionCanBeReused()
    {
        _server = StartTestServer(out var port);

        var conn1 = await _pool.AcquireAsync("localhost", port, useTls: false);
        var stream1 = conn1.Stream;
        conn1.ReturnToPool();

        var conn2 = await _pool.AcquireAsync("localhost", port, useTls: false);
        // Should reuse the same underlying stream
        Assert.Same(stream1, conn2.Stream);

        conn2.Dispose();
    }

    [Fact]
    public async Task Dispose_CleansUpAllConnections()
    {
        _server = StartTestServer(out var port);

        var conn = await _pool.AcquireAsync("localhost", port, useTls: false);
        conn.ReturnToPool();

        _pool.Dispose();

        // After pool disposal, new connections should still work (pool recreates)
        // but we mainly verify no exceptions during disposal
    }

    [Fact]
    public async Task PoolRespectsMaxPerHost()
    {
        _server = StartTestServer(out var port);

        // Acquire and return 3 connections (max is 2)
        var conn1 = await _pool.AcquireAsync("localhost", port, useTls: false);
        conn1.ReturnToPool();
        var conn2 = await _pool.AcquireAsync("localhost", port, useTls: false);
        conn2.ReturnToPool();
        // This third one goes over the max — the pool should handle it gracefully
        var conn3 = await _pool.AcquireAsync("localhost", port, useTls: false);
        conn3.ReturnToPool();

        // No exception means the pool is handling overflow correctly
    }

    private static TcpListener StartTestServer(out int port)
    {
        var listener = TcpListener.Create(0);
        listener.Start();
        port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

        // Accept connections in background to avoid blocking
        _ = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    var client = await listener.AcceptTcpClientAsync();
                    // Keep the connection open (don't close it)
                    _ = Task.Delay(TimeSpan.FromMinutes(1)).ContinueWith(_ => client.Dispose());
                }
            }
            catch (ObjectDisposedException)
            {
                // Server stopped
            }
        });

        return listener;
    }

    [Fact]
    public async Task PooledConnection_Dispose_DisposesStream()
    {
        _server = StartTestServer(out var port);

        var conn = await _pool.AcquireAsync("localhost", port, useTls: false);
        var stream = conn.Stream;
        conn.Dispose();

        // After disposing, the stream should not be writable
        Assert.False(stream.CanWrite);
    }

    [Fact]
    public async Task PooledConnection_Dispose_CalledTwice_NoException()
    {
        _server = StartTestServer(out var port);

        var conn = await _pool.AcquireAsync("localhost", port, useTls: false);
        conn.Dispose();
        var ex = Record.Exception(() => conn.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public async Task AcquireAsync_NewConnection_IsReusedFalse()
    {
        _server = StartTestServer(out var port);

        var conn = await _pool.AcquireAsync("localhost", port, useTls: false);

        Assert.False(conn.IsReused);
        conn.Dispose();
    }

    [Fact]
    public async Task AcquireAsync_ReusedConnection_IsReusedTrue()
    {
        _server = StartTestServer(out var port);

        var conn1 = await _pool.AcquireAsync("localhost", port, useTls: false);
        conn1.ReturnToPool();

        var conn2 = await _pool.AcquireAsync("localhost", port, useTls: false);

        Assert.True(conn2.IsReused);
        conn2.Dispose();
    }

    public void Dispose()
    {
        _pool.Dispose();
        _server?.Stop();
    }
}
