namespace shmoxy.frontend.models;

public class WebSocketFrameInfo
{
    public DateTime Timestamp { get; set; }
    public string Direction { get; set; } = string.Empty;
    public string FrameType { get; set; } = string.Empty;
    public string? Payload { get; set; }
}
