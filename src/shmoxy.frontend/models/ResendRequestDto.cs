namespace shmoxy.frontend.models;

public class ResendRequestDto
{
    public string Method { get; set; } = "GET";
    public string Url { get; set; } = string.Empty;
    public Dictionary<string, string> Headers { get; set; } = new();
    public string? Body { get; set; }
}

public class ResendResponseDto
{
    public int StatusCode { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();
    public string Body { get; set; } = string.Empty;
}
