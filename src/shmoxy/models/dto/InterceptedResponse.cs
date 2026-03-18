namespace shmoxy.models.dto;

/// <summary>
/// Represents a response intercepted by the proxy.
/// </summary>
public class InterceptedResponse
{
    public int StatusCode { get; set; }
    public string? ReasonPhrase { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();
    public byte[] Body { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Gets or sets a value indicating whether to cancel/replace the response.
    /// </summary>
    public bool Cancel { get; set; }
}
