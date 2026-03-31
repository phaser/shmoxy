namespace shmoxy.shared.ipc;

/// <summary>
/// Represents an inspection event captured by the InspectionHook.
/// </summary>
public record InspectionEvent
{
    public DateTime Timestamp { get; init; }
    public string EventType { get; init; } = string.Empty; // "request" | "response" | "passthrough"
    public string Method { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public int? StatusCode { get; init; }
    public Dictionary<string, string> Headers { get; init; } = new();
    public byte[]? Body { get; init; }
    public string? CorrelationId { get; init; }
    public string? FrameType { get; init; }
    public string? Direction { get; init; }
    public bool? IsWebSocket { get; init; }
}
