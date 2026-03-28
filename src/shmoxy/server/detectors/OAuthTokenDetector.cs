using shmoxy.server.interfaces;

namespace shmoxy.server.detectors;

/// <summary>
/// Detects OAuth token endpoint failures that may indicate TLS fingerprint rejection.
/// Triggers when POST to a token endpoint returns a non-JSON error.
/// </summary>
public class OAuthTokenDetector : IPassthroughDetector
{
    public string Id => "oauth";
    public string Name => "OAuth Token Endpoint Detection";

    private static readonly string[] TokenPathPatterns =
    [
        "/token",
        "/connect/token",
        "/oauth2/token",
        "/oauth/token",
        "/auth/token",
        "/oauth2/v1/token",
        "/oauth2/v2/token",
    ];

    public DetectorResult? Analyze(DetectorContext context)
    {
        if (!context.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
            return null;

        if (context.StatusCode is >= 200 and < 300)
            return null;

        if (!IsTokenEndpoint(context.Path))
            return null;

        var responseIsJson = context.ResponseHeaders.TryGetValue("Content-Type", out var ct)
            && ct.Contains("application/json", StringComparison.OrdinalIgnoreCase);

        // If the response is JSON, the endpoint is working — just returning an error
        if (responseIsJson)
            return null;

        return new DetectorResult
        {
            Host = context.Host,
            DetectorId = Id,
            DetectorName = Name,
            Reason = $"OAuth token endpoint {context.Path} returned {context.StatusCode} with non-JSON response — possible TLS fingerprint rejection"
        };
    }

    private static bool IsTokenEndpoint(string path)
    {
        var lowerPath = path.ToLowerInvariant();
        // Strip query string
        var queryIndex = lowerPath.IndexOf('?');
        if (queryIndex >= 0)
            lowerPath = lowerPath[..queryIndex];

        foreach (var pattern in TokenPathPatterns)
        {
            if (lowerPath.EndsWith(pattern))
                return true;
        }

        return false;
    }
}
