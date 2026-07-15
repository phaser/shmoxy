namespace shmoxy.frontend.models;

public class SavedTraceSummary
{
    public string Id { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public int? StatusCode { get; set; }
    public long? DurationMs { get; set; }
    public DateTime Timestamp { get; set; }
    public DateTime SavedAt { get; set; }
    public string? Note { get; set; }
    public bool IsWebSocket { get; set; }
    public long? ResponseBodySize { get; set; }
}

public class SavedTraceData
{
    public string Id { get; set; } = string.Empty;
    public DateTime SavedAt { get; set; }
    public string? Note { get; set; }
    public string Method { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public int? StatusCode { get; set; }
    public long? DurationMs { get; set; }
    public DateTime Timestamp { get; set; }
    public List<KeyValuePair<string, string>>? RequestHeaders { get; set; }
    public List<KeyValuePair<string, string>>? ResponseHeaders { get; set; }
    public string? RequestBody { get; set; }
    public string? ResponseBody { get; set; }
    public string? ResponseBodyBase64 { get; set; }
    public string? ResponseContentType { get; set; }
    public bool IsWebSocket { get; set; }
    public bool WebSocketClosed { get; set; }
    public List<WebSocketFrameInfo>? WebSocketFrames { get; set; }
    public double? TimingConnectMs { get; set; }
    public double? TimingTlsMs { get; set; }
    public double? TimingSendMs { get; set; }
    public double? TimingWaitMs { get; set; }
    public double? TimingReceiveMs { get; set; }
    public bool? TimingConnectionReused { get; set; }
}
