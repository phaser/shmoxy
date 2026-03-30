namespace shmoxy.api.models;

/// <summary>
/// A log entry persisted alongside a saved session for debugging.
/// </summary>
public class InspectionSessionLogEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SessionId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = "Info";
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    public InspectionSession Session { get; set; } = null!;
}
