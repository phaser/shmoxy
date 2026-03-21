namespace shmoxy.shared.ipc;

/// <summary>
/// Configuration for the proxy server.
/// </summary>
public class ProxyConfig
{
    private static readonly string DefaultCertStoragePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "shmoxy");

    public int Port { get; set; } = 8080;
    public string? CertPath { get; set; }
    public string? KeyPath { get; set; }
    public LogLevelEnum LogLevel { get; set; } = LogLevelEnum.Info;
    public int MaxConcurrentConnections { get; set; } = Environment.ProcessorCount * 4;

    /// <summary>
    /// Directory where the root CA certificate is persisted.
    /// Defaults to ~/Library/Application Support/shmoxy on macOS.
    /// If a root CA PFX exists here, it will be loaded instead of generating a new one.
    /// </summary>
    public string CertStoragePath { get; set; } = DefaultCertStoragePath;

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
