using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using shmoxy.api.data;
using shmoxy.api.ipc;
using shmoxy.api.models;
using shmoxy.api.models.configuration;

namespace shmoxy.api.server;

public class RemoteProxyHealthMonitor : IHostedService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<ApiConfig> _config;
    private readonly ILogger<RemoteProxyHealthMonitor> _logger;

    private Timer? _healthCheckTimer;
    private bool _disposed;
    private readonly Dictionary<string, int> _failureCounts = new();

    private const int BaseDelaySeconds = 5;
    private const int MaxDelaySeconds = 300;

    public RemoteProxyHealthMonitor(
        IOptions<ApiConfig> config,
        ILogger<RemoteProxyHealthMonitor> logger,
        IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _config = config;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(_config.Value.HealthCheckIntervalSeconds);
        _healthCheckTimer = new Timer(DoHealthCheck, null, TimeSpan.Zero, interval);
        _logger.LogInformation("Remote proxy health monitor started with {Interval}s interval", interval.TotalSeconds);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _healthCheckTimer?.Change(Timeout.Infinite, 0);
        _logger.LogInformation("Remote proxy health monitor stopped");
        return Task.CompletedTask;
    }

    private async void DoHealthCheck(object? state)
    {
        if (_disposed) return;

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var registry = scope.ServiceProvider.GetRequiredService<IRemoteProxyRegistry>();
            var proxies = await registry.GetAllAsync();
            _logger.LogDebug("Checking health of {Count} remote proxies", proxies.Count);

            foreach (var proxy in proxies)
            {
                await CheckProxyHealthAsync(registry, proxy);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during health check");
        }
    }

    private async Task CheckProxyHealthAsync(IRemoteProxyRegistry registry, RemoteProxy proxy)
    {
        try
        {
            var isHealthy = await CheckHealthWithClientAsync(proxy.AdminUrl, proxy.ApiKey);

            if (isHealthy)
            {
                _failureCounts[proxy.Id] = 0;
                if (proxy.Status != RemoteProxyStatus.Healthy)
                {
                    proxy.Status = RemoteProxyStatus.Healthy;
                    proxy.LastHealthCheck = DateTime.UtcNow;
                    await registry.UpdateAsync(proxy);
                    _logger.LogInformation("Remote proxy {Name} is now healthy", proxy.Name);
                }
            }
            else
            {
                await HandleHealthFailureAsync(registry, proxy);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Health check exception for proxy {Name}", proxy.Name);
            await HandleHealthFailureAsync(registry, proxy);
        }
    }

    private async Task<bool> CheckHealthWithClientAsync(string url, string apiKey)
    {
        var handler = new HttpClientHandler();
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(url),
            Timeout = TimeSpan.FromSeconds(5)
        };
        httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);

        var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        var tempLogger = loggerFactory.CreateLogger<ProxyIpcClient>();
        var tempClient = new ProxyIpcClient(httpClient, tempLogger);
        return await tempClient.IsHealthyAsync();
    }

    private async Task HandleHealthFailureAsync(IRemoteProxyRegistry registry, RemoteProxy proxy)
    {
        if (!_failureCounts.TryGetValue(proxy.Id, out var failures))
        {
            failures = 0;
        }
        failures++;
        _failureCounts[proxy.Id] = failures;

        var delay = Math.Min(BaseDelaySeconds * (int)Math.Pow(2, failures - 1), MaxDelaySeconds);

        proxy.Status = failures > 3 ? RemoteProxyStatus.Unreachable : RemoteProxyStatus.Unhealthy;
        proxy.LastHealthCheck = DateTime.UtcNow;
        await registry.UpdateAsync(proxy);

        _logger.LogWarning("Remote proxy {Name} health check failed (attempt {Failures}, next check in {Delay}s)",
            proxy.Name, failures, delay);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _healthCheckTimer?.Dispose();
        _disposed = true;
    }
}
