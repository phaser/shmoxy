namespace shmoxy.models.dto;

/// <summary>
/// Represents a request intercepted by the proxy.
/// </summary>
public class InterceptedRequest
{
    public string Method { get; set; } = string.Empty;
    public Uri? Url { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 80;
    public string Path { get; set; } = string.Empty;
    public Dictionary<string, string> Headers { get; set; } = new();
    public byte[]? Body { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to cancel the request.
    /// </summary>
    public bool Cancel { get; set; }
}
