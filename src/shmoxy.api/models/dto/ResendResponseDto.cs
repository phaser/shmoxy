namespace shmoxy.api.models.dto;

public class ResendResponseDto
{
    public int StatusCode { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();
    public string Body { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public bool IsBase64 { get; set; }
}
