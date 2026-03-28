using shmoxy.server.detectors;
using shmoxy.server.interfaces;

namespace shmoxy.tests.server.detectors;

public class CloudflareDetectorTests
{
    private readonly CloudflareDetector _detector = new();

    [Fact]
    public void Detect_CloudflareHtmlInsteadOfJson_ReturnsResult()
    {
        var context = CreateContext(
            statusCode: 400,
            responseHeaders: new()
            {
                ["Server"] = "cloudflare",
                ["CF-RAY"] = "abc123",
                ["Content-Type"] = "text/html; charset=UTF-8"
            },
            requestHeaders: new()
            {
                ["Accept"] = "application/json"
            });

        var result = _detector.Analyze(context);

        Assert.NotNull(result);
        Assert.Equal("example.com", result.Host);
        Assert.Equal("cloudflare", result.DetectorId);
        Assert.Contains("HTML instead of expected JSON", result.Reason);
    }

    [Fact]
    public void Detect_Cloudflare403_ReturnsResult()
    {
        var context = CreateContext(
            statusCode: 403,
            responseHeaders: new()
            {
                ["Server"] = "cloudflare",
                ["Content-Type"] = "text/html"
            });

        var result = _detector.Analyze(context);

        Assert.NotNull(result);
        Assert.Contains("403", result.Reason);
    }

    [Fact]
    public void NoDetection_For200Response()
    {
        var context = CreateContext(
            statusCode: 200,
            responseHeaders: new()
            {
                ["Server"] = "cloudflare",
                ["Content-Type"] = "application/json"
            });

        Assert.Null(_detector.Analyze(context));
    }

    [Fact]
    public void NoDetection_WithoutCloudflareHeaders()
    {
        var context = CreateContext(
            statusCode: 403,
            responseHeaders: new()
            {
                ["Server"] = "nginx",
                ["Content-Type"] = "text/html"
            });

        Assert.Null(_detector.Analyze(context));
    }

    [Fact]
    public void Detect_CfRayHeaderWithoutServer()
    {
        var context = CreateContext(
            statusCode: 400,
            responseHeaders: new()
            {
                ["CF-RAY"] = "abc123",
                ["Content-Type"] = "text/html"
            },
            requestHeaders: new()
            {
                ["Accept"] = "application/json"
            });

        var result = _detector.Analyze(context);

        Assert.NotNull(result);
    }

    private static DetectorContext CreateContext(
        int statusCode = 200,
        Dictionary<string, string>? requestHeaders = null,
        Dictionary<string, string>? responseHeaders = null,
        string host = "example.com")
    {
        return new DetectorContext
        {
            Host = host,
            Port = 443,
            Method = "GET",
            Path = "/api/test",
            StatusCode = statusCode,
            RequestHeaders = requestHeaders ?? new(),
            ResponseHeaders = responseHeaders ?? new()
        };
    }
}
