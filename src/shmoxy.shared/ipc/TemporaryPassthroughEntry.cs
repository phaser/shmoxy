namespace shmoxy.shared.ipc;

/// <summary>
/// Represents an active temporary passthrough entry for a domain.
/// When a detector identifies a TLS fingerprint rejection, the domain is temporarily
/// passed through without MITM for a limited number of connections or until timeout.
/// </summary>
public record TemporaryPassthroughEntry
{
    public string Host { get; init; } = string.Empty;
    public string DetectorId { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public int RemainingConnections { get; init; }
    public int MaxConnections { get; init; }
    public DateTime ActivatedAt { get; init; }
    public DateTime ExpiresAt { get; init; }
}
