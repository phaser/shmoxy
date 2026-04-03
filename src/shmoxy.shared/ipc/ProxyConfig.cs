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
    /// When enabled, captures internal proxy activity (hook events, detector triggers,
    /// errors) in a log buffer that is persisted alongside saved sessions.
    /// Disabled by default to avoid overhead when not needed.
    /// </summary>
    public bool SessionLoggingEnabled { get; set; }

    /// <summary>
    /// When enabled, the proxy validates upstream TLS certificates against the system
    /// trust store. Invalid certificates (expired, self-signed, wrong hostname) will
    /// cause the connection to fail. Disabled by default for proxying purposes.
    /// </summary>
    public bool ValidateUpstreamCertificates { get; set; }

    /// <summary>
    /// Maximum number of idle connections to keep per upstream host.
    /// Set to 0 to disable connection pooling (creates a new connection per request).
    /// </summary>
    public int ConnectionPoolSizePerHost { get; set; } = 4;

    /// <summary>
    /// Time in seconds before an idle pooled connection is evicted.
    /// </summary>
    public int ConnectionPoolIdleTimeoutSeconds { get; set; } = 60;

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
