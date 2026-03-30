using System.Net.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using shmoxy.api.ipc;
using shmoxy.api.models;
using shmoxy.api.server;
using shmoxy.shared.ipc;

namespace shmoxy.api.Controllers;

[ApiController]
[Route("api/proxies/{proxyId}/config")]
public class ConfigController : ControllerBase
{
    private readonly IProxyProcessManager _processManager;
    private readonly IRemoteProxyRegistry _registry;
    private readonly IConfigPersistence _configPersistence;
    private readonly ILogger<ConfigController> _logger;

    public ConfigController(
        IProxyProcessManager processManager,
        IRemoteProxyRegistry registry,
        IConfigPersistence configPersistence,
        ILogger<ConfigController> logger)
    {
        _processManager = processManager;
        _registry = registry;
        _configPersistence = configPersistence;
        _logger = logger;
    }

    /// <summary>
    /// Get current proxy configuration.
    /// </summary>
    /// <param name="proxyId">"local" for local proxy, or remote proxy GUID</param>
    [HttpGet]
    public async Task<ActionResult<ProxyConfig>> GetConfig(string proxyId, CancellationToken ct)
    {
        try
        {
            var config = proxyId.Equals("local", StringComparison.OrdinalIgnoreCase)
                ? await GetLocalConfigAsync(ct)
                : await GetRemoteConfigAsync(proxyId, ct);

            return Ok(config);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    /// <summary>
    /// Update proxy configuration. Changes apply immediately without restart.
    /// </summary>
    /// <param name="proxyId">"local" for local proxy, or remote proxy GUID</param>
    /// <param name="config">New configuration values</param>
    [HttpPut]
    public async Task<ActionResult<ProxyConfig>> UpdateConfig(
        string proxyId,
        [FromBody] ProxyConfig config,
        CancellationToken ct)
    {
        if (!ValidateConfig(config, out var errorMessage))
        {
            _logger.LogWarning("Config validation failed for proxy {ProxyId}: {Error}. Config: Port={Port}, LogLevel={LogLevel}, MaxConcurrentConnections={MaxConn}",
                proxyId, errorMessage, config.Port, config.LogLevel, config.MaxConcurrentConnections);
            return BadRequest(new { Message = errorMessage });
        }

        try
        {
            var updatedConfig = proxyId.Equals("local", StringComparison.OrdinalIgnoreCase)
                ? await UpdateLocalConfigAsync(config, ct)
                : await UpdateRemoteConfigAsync(proxyId, config, ct);

            _logger.LogInformation("Updated configuration for proxy {ProxyId}: Port={Port}, LogLevel={LogLevel}",
                proxyId, updatedConfig.Port, updatedConfig.LogLevel);

            return Ok(updatedConfig);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    private async Task<ProxyConfig> GetLocalConfigAsync(CancellationToken ct)
    {
        var state = await _processManager.GetStateAsync();
        if (state?.State == ProxyProcessState.Running)
        {
            return await _processManager.GetIpcClient().GetConfigAsync(ct);
        }

        // When proxy is not running, return persisted config (or defaults)
        return await _configPersistence.LoadAsync(ct) ?? new ProxyConfig();
    }

    private async Task<ProxyConfig> GetRemoteConfigAsync(string proxyId, CancellationToken ct)
    {
        var proxy = await _registry.GetByIdAsync(proxyId, ct);
        if (proxy == null)
        {
            throw new InvalidOperationException($"Remote proxy {proxyId} not found");
        }

        var handler = new HttpClientHandler();
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(proxy.AdminUrl),
            Timeout = TimeSpan.FromSeconds(5)
        };
        httpClient.DefaultRequestHeaders.Add("X-API-Key", proxy.ApiKey);

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var tempLogger = loggerFactory.CreateLogger<ProxyIpcClient>();
        var client = new ProxyIpcClient(httpClient, tempLogger);

        return await client.GetConfigAsync(ct);
    }

    private async Task<ProxyConfig> UpdateLocalConfigAsync(ProxyConfig config, CancellationToken ct)
    {
        var state = await _processManager.GetStateAsync();
        if (state?.State != ProxyProcessState.Running)
        {
            throw new InvalidOperationException("Local proxy must be running to update configuration");
        }

        var currentConfig = await _processManager.GetIpcClient().GetConfigAsync(ct);
        var portChanged = config.Port != currentConfig.Port;

        var updatedConfig = await _processManager.GetIpcClient().UpdateConfigAsync(config, ct);

        // Persist config to disk so it survives restarts
        await _configPersistence.SaveAsync(updatedConfig, ct);

        if (portChanged)
        {
            _logger.LogInformation("Port changed from {OldPort} to {NewPort}, restarting proxy", currentConfig.Port, config.Port);
            await _processManager.RestartAsync(config.Port, ct);

            // Re-apply config after restart (persisted config will be loaded on next cold start)
            updatedConfig = await _processManager.GetIpcClient().UpdateConfigAsync(config, ct);
        }

        return updatedConfig;
    }

    private async Task<ProxyConfig> UpdateRemoteConfigAsync(string proxyId, ProxyConfig config, CancellationToken ct)
    {
        var proxy = await _registry.GetByIdAsync(proxyId, ct);
        if (proxy == null)
        {
            throw new InvalidOperationException($"Remote proxy {proxyId} not found");
        }

        var handler = new HttpClientHandler();
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(proxy.AdminUrl),
            Timeout = TimeSpan.FromSeconds(5)
        };
        httpClient.DefaultRequestHeaders.Add("X-API-Key", proxy.ApiKey);

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var tempLogger = loggerFactory.CreateLogger<ProxyIpcClient>();
        var client = new ProxyIpcClient(httpClient, tempLogger);

        return await client.UpdateConfigAsync(config, ct);
    }

    private static bool ValidateConfig(ProxyConfig config, out string errorMessage)
    {
        if (config.Port < 1 || config.Port > 65535)
        {
            errorMessage = "Port must be between 1 and 65535";
            return false;
        }

        if (config.MaxConcurrentConnections < 1)
        {
            errorMessage = "MaxConcurrentConnections must be at least 1";
            return false;
        }

        if (!Enum.IsDefined(typeof(ProxyConfig.LogLevelEnum), config.LogLevel))
        {
            errorMessage = $"Invalid LogLevel. Must be one of: {string.Join(", ", Enum.GetNames(typeof(ProxyConfig.LogLevelEnum)))}";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }
}
