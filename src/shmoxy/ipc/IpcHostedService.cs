using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using shmoxy.shared.ipc;

namespace shmoxy.ipc;

public class IpcHostedService : IHostedService, IDisposable
{
    private readonly IpcOptions _options;
    private readonly ProxyStateService _stateService;
    private readonly ProxyConfig _config;
    private readonly ILogger<IpcHostedService> _logger;
    private IHost? _ipcHost;
    private bool _disposed;

    public IpcHostedService(
        IOptions<IpcOptions> options,
        ProxyStateService stateService,
        IOptions<ProxyConfig> config,
        ILogger<IpcHostedService> logger)
    {
        _options = options.Value;
        _stateService = stateService;
        _config = config.Value;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var apiKeyService = new ApiKeyService();

        if (_options.AdminPort.HasValue && _options.AdminPort > 0)
        {
            apiKeyService.ApiKey = GenerateApiKey();
            _logger.LogInformation("Admin API Key: {ApiKey} (use with X-API-Key header)", apiKeyService.ApiKey);
        }

        _ipcHost = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseKestrel(kestrelOptions =>
                {
                    if (!string.IsNullOrEmpty(_options.SocketPath))
                    {
                        kestrelOptions.ListenUnixSocket(_options.SocketPath);
                        _logger.LogInformation("IPC API listening on Unix socket: {SocketPath}", _options.SocketPath);
                    }

                    if (_options.AdminPort.HasValue && _options.AdminPort > 0)
                    {
                        kestrelOptions.ListenAnyIP(_options.AdminPort.Value);
                        _logger.LogInformation("Admin API listening on TCP port: {Port}", _options.AdminPort.Value);
                    }
                });
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton(_stateService);
                    services.AddSingleton(apiKeyService);
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseMiddleware<ApiKeyAuthenticationMiddleware>();
                    app.UseEndpoints(endpoints =>
                    {
                        var loggerFactory = app.ApplicationServices.GetRequiredService<ILoggerFactory>();
                        endpoints.MapProxyControlApi(_stateService, _config, loggerFactory);
                    });
                });
            })
            .Build();

        return _ipcHost.StartAsync(cancellationToken);
    }

    private static string GenerateApiKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return Convert.ToHexString(bytes).ToLowerInvariant();
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
    public int? AdminPort { get; init; }
}
