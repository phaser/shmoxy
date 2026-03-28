namespace shmoxy.frontend.models;

public class DetectorInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}

public class PassthroughSuggestionDto
{
    public string Host { get; set; } = string.Empty;
    public string DetectorId { get; set; } = string.Empty;
    public string DetectorName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
