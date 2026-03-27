using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace shmoxy.server;

public class ProxyHostedService : IHostedService
{
    private readonly ProxyServer _server;
    private readonly ILogger<ProxyHostedService> _logger;
    private CancellationTokenSource? _shutdownCts;
    private Task? _proxyTask;

    public ProxyHostedService(
        ProxyServer server,
        ILogger<ProxyHostedService> logger)
    {
        _server = server;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _proxyTask = _server.StartAsync(_shutdownCts.Token);

        // Observe the fire-and-forget task so startup failures are logged
        // instead of being silently swallowed.
        _proxyTask.ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                _logger.LogError(t.Exception, "Proxy server failed to start");
            }
        }, TaskContinuationOptions.OnlyOnFaulted);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Proxy server shutdown requested at {ShutdownRequestedAt}", DateTime.UtcNow);

        if (_shutdownCts != null)
        {
            _shutdownCts.Cancel();
        }

        if (_proxyTask != null)
        {
            await _proxyTask;
        }

        _logger.LogInformation("Proxy server shutdown completed at {ShutdownCompletedAt}", DateTime.UtcNow);
    }

    public ProxyServer GetProxyServer() => _server;
}
