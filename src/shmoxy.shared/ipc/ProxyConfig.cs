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
    /// Hosts that should be tunneled without TLS termination (passthrough).
    /// Supports exact matches (e.g. "example.com") and glob patterns (e.g. "*.example.com").
    /// Passthrough preserves the client's original TLS fingerprint.
    /// </summary>
    public List<string> PassthroughHosts { get; set; } = new();

    /// <summary>
    /// IDs of passthrough detectors that are enabled (e.g. "cloudflare", "waf", "oauth").
    /// Detectors analyze intercepted responses and suggest domains for passthrough.
    /// </summary>
    public List<string> EnabledDetectors { get; set; } = ["cloudflare", "waf", "oauth"];

    /// <summary>
    /// Maximum number of CONNECT requests to pass through during a temporary passthrough window.
    /// </summary>
    public int TempPassthroughMaxConnections { get; set; } = 2;

    /// <summary>
    /// Timeout in seconds for temporary passthrough windows.
    /// </summary>
    public int TempPassthroughTimeoutSeconds { get; set; } = 30;

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
