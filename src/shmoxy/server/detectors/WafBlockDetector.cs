using System.Text;
using shmoxy.server.interfaces;

namespace shmoxy.server.detectors;

/// <summary>
/// Detects WAF (Web Application Firewall) block responses that may indicate
/// TLS fingerprint-based blocking. Checks for common WAF signatures in 403 responses.
/// </summary>
public class WafBlockDetector : IPassthroughDetector
{
    public string Id => "waf";
    public string Name => "WAF Block Detection";

    private static readonly string[] WafSignatures =
    [
        "akamai",
        "aws waf",
        "awselb",
        "imperva",
        "incapsula",
        "access denied",
        "request blocked",
        "web application firewall",
        "security policy",
    ];

    public DetectorResult? Analyze(DetectorContext context)
    {
        if (context.StatusCode != 403)
            return null;

        var isHtml = context.ResponseHeaders.TryGetValue("Content-Type", out var ct)
            && ct.Contains("text/html", StringComparison.OrdinalIgnoreCase);

        if (!isHtml)
            return null;

        // Check response headers for WAF signatures
        var wafHeader = DetectWafInHeaders(context.ResponseHeaders);
        if (wafHeader != null)
        {
            return new DetectorResult
            {
                Host = context.Host,
                DetectorId = Id,
                DetectorName = Name,
                Reason = $"WAF block detected via header: {wafHeader}"
            };
        }

        // Check response body for WAF signatures (lightweight — only first 4KB)
        if (context.ResponseBody is { Length: > 0 })
        {
            var bodySnippet = Encoding.UTF8.GetString(
                context.ResponseBody, 0, Math.Min(context.ResponseBody.Length, 4096));

            var signature = DetectWafInBody(bodySnippet);
            if (signature != null)
            {
                return new DetectorResult
                {
                    Host = context.Host,
                    DetectorId = Id,
                    DetectorName = Name,
                    Reason = $"WAF block detected in response body: matched \"{signature}\""
                };
            }
        }

        return null;
    }

    private static string? DetectWafInHeaders(Dictionary<string, string> headers)
    {
        foreach (var (key, value) in headers)
        {
            if (key.Equals("Server", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var sig in WafSignatures)
                {
                    if (value.Contains(sig, StringComparison.OrdinalIgnoreCase))
                        return $"Server: {value}";
                }
            }

            if (key.Equals("X-CDN", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("X-Akamai", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("X-Incapsula", StringComparison.OrdinalIgnoreCase))
            {
                return $"{key}: {value}";
            }
        }

        return null;
    }

    private static string? DetectWafInBody(string body)
    {
        var lowerBody = body.ToLowerInvariant();
        foreach (var sig in WafSignatures)
        {
            if (lowerBody.Contains(sig))
                return sig;
        }

        return null;
    }
}
