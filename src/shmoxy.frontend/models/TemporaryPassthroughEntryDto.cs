namespace shmoxy.frontend.models;

public class TemporaryPassthroughEntryDto
{
    public string Host { get; set; } = string.Empty;
    public string DetectorId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public int RemainingConnections { get; set; }
    public int MaxConnections { get; set; }
    public DateTime ActivatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
