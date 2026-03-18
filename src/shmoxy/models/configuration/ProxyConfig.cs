namespace shmoxy.models.configuration;

/// <summary>
/// Configuration for the proxy server.
/// </summary>
public class ProxyConfig
{
    public int Port { get; set; } = 8080;
    public string? CertPath { get; set; }
    public string? KeyPath { get; set; }
    public LogLevelEnum LogLevel { get; set; } = LogLevelEnum.Info;

    /// <summary>
    /// Logging levels.
    /// </summary>
    public enum LogLevelEnum
    {
        Debug,
        Info,
        Warn,
        Error
    }
}
