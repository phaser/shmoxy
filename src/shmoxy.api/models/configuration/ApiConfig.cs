namespace shmoxy.api.models.configuration;

/// <summary>
/// Configuration for the API server.
/// </summary>
public class ApiConfig
{
    public int Port { get; set; } = 5000;
    public int ProxyPort { get; set; } = 8080;
    public string? ProxyIpcSocketPath { get; set; }
    public bool AutoStartProxy { get; set; } = false;
}
