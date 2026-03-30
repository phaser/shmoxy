namespace shmoxy.api.models.dto;

public class CreateSessionRequest
{
    public string Name { get; set; } = string.Empty;
    public List<SessionRowDto> Rows { get; set; } = new();
    public List<SessionLogEntryDto>? LogEntries { get; set; }
}

public class SessionResponse
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int RowCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class SessionRowDto
{
    public string Method { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public int? StatusCode { get; set; }
    public long? DurationMs { get; set; }
    public DateTime Timestamp { get; set; }
    public Dictionary<string, string>? RequestHeaders { get; set; }
    public Dictionary<string, string>? ResponseHeaders { get; set; }
    public string? RequestBody { get; set; }
    public string? ResponseBody { get; set; }
}

public class UpdateSessionRequest
{
    public List<SessionRowDto> Rows { get; set; } = new();
    public List<SessionLogEntryDto>? LogEntries { get; set; }
}

public class SessionLogEntryDto
{
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = "Info";
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
