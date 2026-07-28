namespace shmoxy.api.models.configuration;

/// <summary>
/// Configuration for the API server.
/// </summary>
public class ApiConfig
{
    public int Port { get; set; } = 5000;
    public int ProxyPort { get; set; } = 8080;
    public string? ProxyIpcSocketPath { get; set; }
    public string? ProxyBinaryPath { get; set; } = "shmoxy";
    public bool AutoStartProxy { get; set; } = true;
    public string? ConnectionString { get; set; }
    public int HealthCheckIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Directory holding API state that must survive a restart: the data protection
    /// keys and the SQLite database. Defaults to the platform application-data
    /// directory. Containers must set this to a path backed by a mounted volume --
    /// otherwise the database and antiforgery keys live in the container's writable
    /// layer and are destroyed whenever the container is recreated.
    /// </summary>
    public string? DataDirectory { get; set; }
}
