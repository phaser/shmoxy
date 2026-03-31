namespace shmoxy.api.models;

public class InspectionSessionWebSocketFrame
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SessionRowId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string Direction { get; set; } = string.Empty;
    public string FrameType { get; set; } = string.Empty;
    public string? Payload { get; set; }

    public InspectionSessionRow SessionRow { get; set; } = null!;
}
