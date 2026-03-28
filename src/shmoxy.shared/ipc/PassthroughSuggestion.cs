namespace shmoxy.shared.ipc;

/// <summary>
/// A suggestion from a passthrough detector that a domain should be added to the passthrough list.
/// </summary>
public record PassthroughSuggestion
{
    public DateTime Timestamp { get; init; }
    public string Host { get; init; } = string.Empty;
    public string DetectorId { get; init; } = string.Empty;
    public string DetectorName { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}
