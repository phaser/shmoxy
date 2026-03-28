using shmoxy.server.detectors;
using shmoxy.server.interfaces;

namespace shmoxy.tests.server.detectors;

public class OAuthTokenDetectorTests
{
    private readonly OAuthTokenDetector _detector = new();

    [Theory]
    [InlineData("/oauth2/token")]
    [InlineData("/connect/token")]
    [InlineData("/auth/token")]
    [InlineData("/api/oauth2/v1/token")]
    public void Detect_TokenEndpoint_NonJsonError(string path)
    {
        var context = new DetectorContext
        {
            Host = "auth.example.com",
            Port = 443,
            Method = "POST",
            Path = path,
            StatusCode = 403,
            RequestHeaders = new(),
            ResponseHeaders = new()
            {
                ["Content-Type"] = "text/html"
            }
        };

        var result = _detector.Analyze(context);

        Assert.NotNull(result);
        Assert.Equal("oauth", result.DetectorId);
        Assert.Contains("token endpoint", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoDetection_WhenResponseIsJson()
    {
        var context = new DetectorContext
        {
            Host = "auth.example.com",
            Port = 443,
            Method = "POST",
            Path = "/oauth2/token",
            StatusCode = 400,
            RequestHeaders = new(),
            ResponseHeaders = new()
            {
                ["Content-Type"] = "application/json"
            }
        };

        Assert.Null(_detector.Analyze(context));
    }

    [Fact]
    public void NoDetection_ForGetRequest()
    {
        var context = new DetectorContext
        {
            Host = "auth.example.com",
            Port = 443,
            Method = "GET",
            Path = "/oauth2/token",
            StatusCode = 403,
            RequestHeaders = new(),
            ResponseHeaders = new()
            {
                ["Content-Type"] = "text/html"
            }
        };

        Assert.Null(_detector.Analyze(context));
    }

    [Fact]
    public void NoDetection_ForNonTokenPath()
    {
        var context = new DetectorContext
        {
            Host = "api.example.com",
            Port = 443,
            Method = "POST",
            Path = "/api/data",
            StatusCode = 403,
            RequestHeaders = new(),
            ResponseHeaders = new()
            {
                ["Content-Type"] = "text/html"
            }
        };

        Assert.Null(_detector.Analyze(context));
    }

    [Fact]
    public void NoDetection_For200Response()
    {
        var context = new DetectorContext
        {
            Host = "auth.example.com",
            Port = 443,
            Method = "POST",
            Path = "/oauth2/token",
            StatusCode = 200,
            RequestHeaders = new(),
            ResponseHeaders = new()
            {
                ["Content-Type"] = "text/html"
            }
        };

        Assert.Null(_detector.Analyze(context));
    }

    [Fact]
    public void Detect_TokenPath_WithQueryString()
    {
        var context = new DetectorContext
        {
            Host = "auth.example.com",
            Port = 443,
            Method = "POST",
            Path = "/oauth2/token?grant_type=client_credentials",
            StatusCode = 403,
            RequestHeaders = new(),
            ResponseHeaders = new()
            {
                ["Content-Type"] = "text/html"
            }
        };

        var result = _detector.Analyze(context);

        Assert.NotNull(result);
    }
}
