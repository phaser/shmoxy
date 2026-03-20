using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using shmoxy.models.configuration;

namespace shmoxy.ipc;

public class IpcHostedService : IHostedService, IDisposable
{
    private readonly string _socketPath;
    private readonly ProxyStateService _stateService;
    private readonly ProxyConfig _config;
    private readonly ILogger<IpcHostedService> _logger;
    private IWebHost? _ipcHost;
    private bool _disposed;

    public IpcHostedService(
        IOptions<IpcOptions> options,
        ProxyStateService stateService,
        IOptions<ProxyConfig> config,
        ILogger<IpcHostedService> logger)
    {
        _socketPath = options.Value.SocketPath!;
        _stateService = stateService;
        _config = config.Value;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ipcHost = new WebHostBuilder()
            .UseKestrel(kestrelOptions =>
            {
                kestrelOptions.ListenUnixSocket(_socketPath);
            })
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton(_stateService);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapProxyControlApi(_stateService, _config);
                });
            })
            .Build();

        _logger.LogInformation("IPC API listening on {SocketPath}", _socketPath);
        return _ipcHost.StartAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_ipcHost != null)
        {
            await _ipcHost.StopAsync(cancellationToken);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _ipcHost?.Dispose();
        _disposed = true;
    }
}

public record IpcOptions
{
    public string? SocketPath { get; init; }
}
