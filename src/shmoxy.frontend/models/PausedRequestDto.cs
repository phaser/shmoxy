namespace shmoxy.frontend.models;

public class PausedRequestDto
{
    public string CorrelationId { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string? Url { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();
    public string? Body { get; set; }
    public DateTime PausedAt { get; set; }
}
