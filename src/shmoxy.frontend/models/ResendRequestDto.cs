namespace shmoxy.frontend.models;

public class ResendRequestDto
{
    public string Method { get; set; } = "GET";
    public string Url { get; set; } = string.Empty;
    public List<KeyValuePair<string, string>> Headers { get; set; } = new();
    public string? Body { get; set; }
}

public class ResendResponseDto
{
    public int StatusCode { get; set; }
    public List<KeyValuePair<string, string>> Headers { get; set; } = new();
    public string Body { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public bool IsBase64 { get; set; }
}
