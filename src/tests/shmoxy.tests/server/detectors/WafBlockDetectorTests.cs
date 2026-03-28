using System.Text;
using shmoxy.server.detectors;
using shmoxy.server.interfaces;

namespace shmoxy.tests.server.detectors;

public class WafBlockDetectorTests
{
    private readonly WafBlockDetector _detector = new();

    [Fact]
    public void Detect_AkamaiWafHeader()
    {
        var context = CreateContext(
            statusCode: 403,
            responseHeaders: new()
            {
                ["Server"] = "AkamaiGHost",
                ["Content-Type"] = "text/html"
            });

        var result = _detector.Analyze(context);

        Assert.NotNull(result);
        Assert.Equal("waf", result.DetectorId);
        Assert.Contains("Akamai", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Detect_WafSignatureInBody()
    {
        var body = "<html><body>Access Denied. Your request was blocked by our security policy.</body></html>";

        var context = CreateContext(
            statusCode: 403,
            responseHeaders: new()
            {
                ["Content-Type"] = "text/html"
            },
            responseBody: Encoding.UTF8.GetBytes(body));

        var result = _detector.Analyze(context);

        Assert.NotNull(result);
        Assert.Contains("access denied", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoDetection_For200()
    {
        var context = CreateContext(statusCode: 200);
        Assert.Null(_detector.Analyze(context));
    }

    [Fact]
    public void NoDetection_For403_NonHtml()
    {
        var context = CreateContext(
            statusCode: 403,
            responseHeaders: new()
            {
                ["Content-Type"] = "application/json"
            });

        Assert.Null(_detector.Analyze(context));
    }

    [Fact]
    public void Detect_IncapsulaHeader()
    {
        var context = CreateContext(
            statusCode: 403,
            responseHeaders: new()
            {
                ["X-Incapsula-Something"] = "yes",
                ["Content-Type"] = "text/html"
            });

        var result = _detector.Analyze(context);

        Assert.NotNull(result);
    }

    private static DetectorContext CreateContext(
        int statusCode = 200,
        Dictionary<string, string>? responseHeaders = null,
        byte[]? responseBody = null)
    {
        return new DetectorContext
        {
            Host = "example.com",
            Port = 443,
            Method = "GET",
            Path = "/",
            StatusCode = statusCode,
            RequestHeaders = new(),
            ResponseHeaders = responseHeaders ?? new(),
            ResponseBody = responseBody
        };
    }
}
