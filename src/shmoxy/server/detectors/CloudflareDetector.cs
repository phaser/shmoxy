using shmoxy.server.interfaces;

namespace shmoxy.server.detectors;

/// <summary>
/// Detects Cloudflare bot detection responses that indicate TLS fingerprint rejection.
/// Triggers when: response has Server: cloudflare, CF-RAY header, status 400/403,
/// and Content-Type: text/html on a request that expected application/json.
/// </summary>
public class CloudflareDetector : IPassthroughDetector
{
    public string Id => "cloudflare";
    public string Name => "Cloudflare Bot Detection";

    public DetectorResult? Analyze(DetectorContext context)
    {
        if (context.StatusCode is not (400 or 403))
            return null;

        var hasCloudflareServer = context.ResponseHeaders.TryGetValue("Server", out var server)
            && server.Contains("cloudflare", StringComparison.OrdinalIgnoreCase);

        var hasCfRay = context.ResponseHeaders.ContainsKey("CF-RAY")
            || context.ResponseHeaders.ContainsKey("cf-ray");

        if (!hasCloudflareServer && !hasCfRay)
            return null;

        var responseIsHtml = context.ResponseHeaders.TryGetValue("Content-Type", out var contentType)
            && contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase);

        var requestExpectedJson = context.RequestHeaders.TryGetValue("Accept", out var accept)
            && accept.Contains("application/json", StringComparison.OrdinalIgnoreCase);

        // Cloudflare returning HTML when JSON was expected, with a 400/403, is a strong signal
        if (responseIsHtml && requestExpectedJson)
        {
            return new DetectorResult
            {
                Host = context.Host,
                DetectorId = Id,
                DetectorName = Name,
                Reason = $"Cloudflare returned {context.StatusCode} HTML instead of expected JSON — likely TLS fingerprint rejection"
            };
        }

        // Even without the accept mismatch, a 403 from Cloudflare is suspicious
        if (context.StatusCode == 403 && hasCloudflareServer)
        {
            return new DetectorResult
            {
                Host = context.Host,
                DetectorId = Id,
                DetectorName = Name,
                Reason = $"Cloudflare returned 403 — possible TLS fingerprint rejection"
            };
        }

        return null;
    }
}
