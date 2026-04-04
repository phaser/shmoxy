namespace shmoxy.shared.ipc;

/// <summary>
/// Timing breakdown for an HTTP request/response cycle.
/// Nullable phases indicate the phase was skipped (e.g., reused connection).
/// </summary>
public record TimingInfo
{
    /// <summary>DNS resolution + TCP connect (null if connection was reused from pool).</summary>
    public double? ConnectMs { get; init; }

    /// <summary>TLS handshake duration (null if no TLS or connection was reused).</summary>
    public double? TlsMs { get; init; }

    /// <summary>Time to write the request and flush to the network.</summary>
    public double SendMs { get; init; }

    /// <summary>Time to first byte: from flush completion to first response data.</summary>
    public double WaitMs { get; init; }

    /// <summary>Content transfer: first response byte to last response byte.</summary>
    public double ReceiveMs { get; init; }

    /// <summary>Whether the connection was reused from the pool.</summary>
    public bool Reused { get; init; }
}
