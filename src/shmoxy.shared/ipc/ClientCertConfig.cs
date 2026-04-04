namespace shmoxy.shared.ipc;

/// <summary>
/// Configuration for a client certificate used in mTLS connections to upstream servers.
/// </summary>
public class ClientCertConfig
{
    /// <summary>
    /// Host pattern to match (e.g., "api.example.com", "*.internal.corp").
    /// Supports exact match and glob patterns with *.
    /// </summary>
    public string HostPattern { get; set; } = string.Empty;

    /// <summary>
    /// Path to the PFX/PKCS#12 certificate file.
    /// </summary>
    public string CertPath { get; set; } = string.Empty;

    /// <summary>
    /// Optional password for the PFX file.
    /// </summary>
    public string? Password { get; set; }
}
