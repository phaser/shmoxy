namespace shmoxy.api.models;

public class SavedTraceWebSocketFrame
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SavedTraceId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string Direction { get; set; } = string.Empty;
    public string FrameType { get; set; } = string.Empty;
    public string? Payload { get; set; }

    public SavedTrace SavedTrace { get; set; } = null!;
}
