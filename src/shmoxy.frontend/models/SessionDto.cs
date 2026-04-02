namespace shmoxy.frontend.models;

public class SessionSummary
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int RowCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class SessionRowData
{
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
}

public class SessionLogEntryData
{
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = "Info";
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
