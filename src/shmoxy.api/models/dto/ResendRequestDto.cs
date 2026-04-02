namespace shmoxy.api.models.dto;

public class ResendRequestDto
{
    public string Method { get; set; } = "GET";
    public string Url { get; set; } = string.Empty;
    public List<KeyValuePair<string, string>> Headers { get; set; } = new();
    public string? Body { get; set; }
}
