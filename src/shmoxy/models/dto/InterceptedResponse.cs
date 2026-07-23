using shmoxy.shared.ipc;

namespace shmoxy.models.dto;

/// <summary>
/// Represents a response intercepted by the proxy.
/// </summary>
public class InterceptedResponse
{
    public int StatusCode { get; set; }
    public string? ReasonPhrase { get; set; }
    public List<KeyValuePair<string, string>> Headers { get; set; } = new();

    /// <summary>
    /// Complete body when it fits the configured capture limit; otherwise a
    /// bounded preview.
    /// </summary>
    public byte[] Body { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Total payload bytes transferred, excluding HTTP chunk framing.
    /// </summary>
    public long BodyLength { get; set; }

    /// <summary>
    /// Indicates that <see cref="Body"/> contains only a preview.
    /// </summary>
    public bool BodyTruncated { get; set; }

    /// <summary>
    /// Original Content-Encoding header value, when present.
    /// </summary>
    public string? ContentEncoding { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to cancel/replace the response.
    /// </summary>
    public bool Cancel { get; set; }

    /// <summary>
    /// Unique ID linking this response to its corresponding request for inspection.
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Timing breakdown for the request/response cycle.
    /// </summary>
    public TimingInfo? Timing { get; set; }
}
