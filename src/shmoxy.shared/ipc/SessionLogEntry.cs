namespace shmoxy.shared.ipc;

/// <summary>
/// A single log entry captured during proxy operation for session debugging.
/// </summary>
public class SessionLogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Level { get; set; } = "Info";
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
