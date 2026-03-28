using shmoxy.models.dto;
using shmoxy.server.detectors;
using shmoxy.server.hooks;

namespace shmoxy.tests.server.hooks;

public class PassthroughDetectorHookTests
{
    [Fact]
    public async Task EmitsSuggestion_WhenDetectorTriggered()
    {
        var hook = new PassthroughDetectorHook();
        hook.AddDetector(new CloudflareDetector());
        hook.SetDetectorEnabled("cloudflare", true);

        var request = new InterceptedRequest
        {
            Method = "GET",
            Host = "api.example.com",
            Port = 443,
            Path = "/data",
            Headers = new() { ["Accept"] = "application/json" },
            CorrelationId = "test-1"
        };

        var response = new InterceptedResponse
        {
            StatusCode = 400,
            Headers = new()
            {
                ["Server"] = "cloudflare",
                ["CF-RAY"] = "abc",
                ["Content-Type"] = "text/html"
            },
            CorrelationId = "test-1"
        };

        await hook.OnRequestAsync(request);
        await hook.OnResponseAsync(response);

        var reader = hook.GetSuggestionReader();
        Assert.True(reader.TryRead(out var suggestion));
        Assert.Equal("api.example.com", suggestion.Host);
        Assert.Equal("cloudflare", suggestion.DetectorId);
    }

    [Fact]
    public async Task NoSuggestion_WhenDetectorDisabled()
    {
        var hook = new PassthroughDetectorHook();
        hook.AddDetector(new CloudflareDetector());
        // cloudflare is disabled by default

        var request = new InterceptedRequest
        {
            Method = "GET",
            Host = "api.example.com",
            Port = 443,
            Path = "/data",
            Headers = new() { ["Accept"] = "application/json" },
            CorrelationId = "test-1"
        };

        var response = new InterceptedResponse
        {
            StatusCode = 400,
            Headers = new()
            {
                ["Server"] = "cloudflare",
                ["Content-Type"] = "text/html"
            },
            CorrelationId = "test-1"
        };

        await hook.OnRequestAsync(request);
        await hook.OnResponseAsync(response);

        var reader = hook.GetSuggestionReader();
        Assert.False(reader.TryRead(out _));
    }

    [Fact]
    public async Task DismissedHost_NotSuggestedAgain()
    {
        var hook = new PassthroughDetectorHook();
        hook.AddDetector(new CloudflareDetector());
        hook.SetDetectorEnabled("cloudflare", true);
        hook.DismissSuggestion("api.example.com");

        var request = new InterceptedRequest
        {
            Method = "GET",
            Host = "api.example.com",
            Port = 443,
            Path = "/data",
            Headers = new() { ["Accept"] = "application/json" },
            CorrelationId = "test-1"
        };

        var response = new InterceptedResponse
        {
            StatusCode = 400,
            Headers = new()
            {
                ["Server"] = "cloudflare",
                ["CF-RAY"] = "abc",
                ["Content-Type"] = "text/html"
            },
            CorrelationId = "test-1"
        };

        await hook.OnRequestAsync(request);
        await hook.OnResponseAsync(response);

        var reader = hook.GetSuggestionReader();
        Assert.False(reader.TryRead(out _));
    }

    [Fact]
    public async Task SameHost_NotSuggestedTwice()
    {
        var hook = new PassthroughDetectorHook();
        hook.AddDetector(new CloudflareDetector());
        hook.SetDetectorEnabled("cloudflare", true);

        for (int i = 0; i < 2; i++)
        {
            var request = new InterceptedRequest
            {
                Method = "GET",
                Host = "api.example.com",
                Port = 443,
                Path = "/data",
                Headers = new() { ["Accept"] = "application/json" },
                CorrelationId = $"test-{i}"
            };

            var response = new InterceptedResponse
            {
                StatusCode = 400,
                Headers = new()
                {
                    ["Server"] = "cloudflare",
                    ["CF-RAY"] = "abc",
                    ["Content-Type"] = "text/html"
                },
                CorrelationId = $"test-{i}"
            };

            await hook.OnRequestAsync(request);
            await hook.OnResponseAsync(response);
        }

        var reader = hook.GetSuggestionReader();
        Assert.True(reader.TryRead(out _));
        Assert.False(reader.TryRead(out _));
    }

    [Fact]
    public void GetDetectors_ReturnsRegisteredDetectors()
    {
        var hook = new PassthroughDetectorHook();
        hook.AddDetector(new CloudflareDetector());
        hook.AddDetector(new WafBlockDetector());

        var detectors = hook.GetDetectors();

        Assert.Equal(2, detectors.Count);
        Assert.Contains(detectors, d => d.Id == "cloudflare");
        Assert.Contains(detectors, d => d.Id == "waf");
        Assert.All(detectors, d => Assert.False(d.Enabled)); // disabled by default
    }

    [Fact]
    public void SetDetectorEnabled_ChangesState()
    {
        var hook = new PassthroughDetectorHook();
        hook.AddDetector(new CloudflareDetector());

        hook.SetDetectorEnabled("cloudflare", true);

        var detectors = hook.GetDetectors();
        Assert.True(detectors.Single(d => d.Id == "cloudflare").Enabled);
    }
}
