namespace shmoxy.api.models;

public class InspectionSessionRow
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SessionId { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public int? StatusCode { get; set; }
    public long? DurationMs { get; set; }
    public DateTime Timestamp { get; set; }
    public string? RequestHeaders { get; set; }
    public string? ResponseHeaders { get; set; }
    public string? RequestBody { get; set; }
    public string? ResponseBody { get; set; }
    public string? ResponseBodyBase64 { get; set; }
    public string? ResponseContentType { get; set; }
    public bool IsWebSocket { get; set; }
    public bool WebSocketClosed { get; set; }
    public double? TimingConnectMs { get; set; }
    public double? TimingTlsMs { get; set; }
    public double? TimingSendMs { get; set; }
    public double? TimingWaitMs { get; set; }
    public double? TimingReceiveMs { get; set; }
    public bool? TimingConnectionReused { get; set; }

    public InspectionSession Session { get; set; } = null!;
    public List<InspectionSessionWebSocketFrame> WebSocketFrames { get; set; } = new();
}
