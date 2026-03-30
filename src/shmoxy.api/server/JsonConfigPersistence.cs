using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using shmoxy.shared.ipc;

namespace shmoxy.api.server;

public class JsonConfigPersistence : IConfigPersistence
{
    private readonly string _configFilePath;
    private readonly ILogger<JsonConfigPersistence> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonConfigPersistence(ILogger<JsonConfigPersistence> logger, string? configDirectory = null)
    {
        var directory = configDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "shmoxy");
        _configFilePath = Path.Combine(directory, "proxy-config.json");
        _logger = logger;
    }

    public async Task<ProxyConfig?> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_configFilePath))
        {
            _logger.LogDebug("No persisted config file found at {Path}", _configFilePath);
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_configFilePath, ct);
            var config = JsonSerializer.Deserialize<ProxyConfig>(json, JsonOptions);
            _logger.LogInformation("Loaded persisted config from {Path}", _configFilePath);
            return config;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load persisted config from {Path}", _configFilePath);
            return null;
        }
    }

    public async Task SaveAsync(ProxyConfig config, CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(_configFilePath)!;
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(config, JsonOptions);
        await File.WriteAllTextAsync(_configFilePath, json, ct);
        _logger.LogInformation("Persisted config to {Path}", _configFilePath);
    }
}
