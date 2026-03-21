using shmoxy.server;
using shmoxy.shared.ipc;

namespace shmoxy.tests;

/// <summary>
/// Test fixture for proxy server tests.
/// Starts the server on an OS-assigned port and runs it in the background.
/// Shared across all tests in the class via IClassFixture.
/// </summary>
public class ProxyTestFixture : IAsyncLifetime, IDisposable
{
    public ProxyConfig Config { get; }
    public ProxyServer Server { get; }

    private CancellationTokenSource _cts = new();
    private Task? _serverTask;
    private bool _disposed;

    public ProxyTestFixture()
    {
        Config = new ProxyConfig
        {
            Port = 0, // OS-assigned port to avoid conflicts
            LogLevel = ProxyConfig.LogLevelEnum.Warn
        };

        Server = new ProxyServer(Config);
    }

    /// <summary>
    /// Starts the proxy server on a background task.
    /// Called automatically by xUnit before any test in the class runs.
    /// </summary>
    public async Task InitializeAsync()
    {
        _serverTask = Server.StartAsync(_cts.Token);

        // Wait until the server is actually listening
        var timeout = Task.Delay(TimeSpan.FromSeconds(5));
        while (!Server.IsListening)
        {
            if (timeout.IsCompleted)
                throw new TimeoutException("Proxy server did not start within 5 seconds");
            await Task.Delay(50);
        }
    }

    /// <summary>
    /// Stops the proxy server. Called automatically by xUnit after all tests complete.
    /// </summary>
    public async Task DisposeAsync()
    {
        await Server.StopAsync();
        _cts.Cancel();

        if (_serverTask != null)
        {
            try
            {
                await _serverTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation stops the server loop
            }
            catch (TimeoutException)
            {
                // Server task didn't complete in time, proceed with disposal
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _cts.Cancel();
        _cts.Dispose();
        Server.Dispose();
        _disposed = true;
    }
}
