using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using shmoxy.api.models.configuration;
using shmoxy.api.server;

namespace shmoxy.api.server;

public class ProxyHostedService : IHostedService
{
    private readonly ILogger<ProxyHostedService> _logger;
    private readonly IProxyProcessManager _processManager;
    private readonly IOptions<ApiConfig> _config;

    public ProxyHostedService(
        ILogger<ProxyHostedService> logger,
        IProxyProcessManager processManager,
        IOptions<ApiConfig> config)
    {
        _logger = logger;
        _processManager = processManager;
        _config = config;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_config.Value.AutoStartProxy)
        {
            _logger.LogInformation("Auto-starting proxy on application startup");
            try
            {
                var state = await _processManager.StartAsync(cancellationToken);
                _logger.LogInformation("Proxy auto-started with state {State}", state.State);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to auto-start proxy");
                throw;
            }
        }
        else
        {
            _logger.LogInformation("Auto-start is disabled, proxy will not be started automatically");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping proxy on application shutdown");
        try
        {
            await _processManager.StopAsync(cancellationToken);
            _logger.LogInformation("Proxy stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping proxy during shutdown");
        }
    }
}
