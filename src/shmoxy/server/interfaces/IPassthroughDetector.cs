namespace shmoxy.server.interfaces;

/// <summary>
/// Interface for auto-detecting domains that should bypass MITM via TLS passthrough.
/// Detectors analyze intercepted request/response pairs and flag domains as candidates.
/// </summary>
public interface IPassthroughDetector
{
    /// <summary>
    /// Unique identifier for this detector (e.g. "cloudflare", "waf", "oauth").
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Human-readable name for display in the UI.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Analyzes an intercepted response (paired with its request) to determine
    /// if the domain should be added to the passthrough list.
    /// Returns a result if the domain is a passthrough candidate, null otherwise.
    /// </summary>
    DetectorResult? Analyze(DetectorContext context);
}

/// <summary>
/// Context passed to detectors containing the request/response pair.
/// </summary>
public class DetectorContext
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required string Method { get; init; }
    public required string Path { get; init; }
    public required int StatusCode { get; init; }
    public required Dictionary<string, string> RequestHeaders { get; init; }
    public required Dictionary<string, string> ResponseHeaders { get; init; }
    public byte[]? ResponseBody { get; init; }
}

/// <summary>
/// Result from a passthrough detector indicating a domain should be considered for passthrough.
/// </summary>
public class DetectorResult
{
    public required string Host { get; init; }
    public required string DetectorId { get; init; }
    public required string DetectorName { get; init; }
    public required string Reason { get; init; }
}
