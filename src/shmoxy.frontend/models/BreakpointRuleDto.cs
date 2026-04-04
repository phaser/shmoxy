namespace shmoxy.frontend.models;

public class BreakpointRuleDto
{
    public string Id { get; set; } = string.Empty;
    public string? Method { get; set; }
    public string UrlPattern { get; set; } = string.Empty;
    public bool IsRegex { get; set; }
}
