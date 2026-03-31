using shmoxy.models.dto;
using shmoxy.server.hooks;

namespace shmoxy.tests.server.hooks;

public class BreakpointHookTests
{
    [Fact]
    public async Task OnRequestAsync_PassesThrough_WhenDisabled()
    {
        var hook = new BreakpointHook { Enabled = false };
        var request = new InterceptedRequest
        {
            Method = "GET",
            Url = new Uri("http://example.com"),
            CorrelationId = "test-1"
        };

        var result = await hook.OnRequestAsync(request);

        Assert.Same(request, result);
    }

    [Fact]
    public async Task OnRequestAsync_PausesAndReleases()
    {
        var hook = new BreakpointHook { Enabled = true, TimeoutMs = 5000 };
        var request = new InterceptedRequest
        {
            Method = "GET",
            Url = new Uri("http://example.com"),
            CorrelationId = "test-2"
        };

        var pauseTask = hook.OnRequestAsync(request);

        // Request should be paused
        await Task.Delay(50);
        Assert.False(pauseTask.IsCompleted);
        Assert.Single(hook.GetPausedRequests());

        // Release it
        var released = hook.Release("test-2");
        Assert.True(released);

        var result = await pauseTask;
        Assert.Same(request, result);
        Assert.Empty(hook.GetPausedRequests());
    }

    [Fact]
    public async Task OnRequestAsync_DropReturnsNull()
    {
        var hook = new BreakpointHook { Enabled = true, TimeoutMs = 5000 };
        var request = new InterceptedRequest
        {
            Method = "POST",
            Url = new Uri("http://example.com/api"),
            CorrelationId = "test-3"
        };

        var pauseTask = hook.OnRequestAsync(request);

        await Task.Delay(50);
        hook.Drop("test-3");

        var result = await pauseTask;
        Assert.Null(result);
    }

    [Fact]
    public async Task OnRequestAsync_TimesOutAndForwards()
    {
        var hook = new BreakpointHook { Enabled = true, TimeoutMs = 200 };
        var request = new InterceptedRequest
        {
            Method = "GET",
            Url = new Uri("http://example.com"),
            CorrelationId = "test-4"
        };

        var result = await hook.OnRequestAsync(request);

        // After timeout, should auto-forward the original request
        Assert.Same(request, result);
    }

    [Fact]
    public async Task Release_WithModifiedRequest_ForwardsModified()
    {
        var hook = new BreakpointHook { Enabled = true, TimeoutMs = 5000 };
        var original = new InterceptedRequest
        {
            Method = "GET",
            Url = new Uri("http://example.com"),
            CorrelationId = "test-5"
        };

        var pauseTask = hook.OnRequestAsync(original);
        await Task.Delay(50);

        var modified = new InterceptedRequest
        {
            Method = "POST",
            Url = new Uri("http://example.com/modified"),
            CorrelationId = "test-5"
        };

        hook.Release("test-5", modified);
        var result = await pauseTask;

        Assert.Same(modified, result);
        Assert.Equal("POST", result!.Method);
    }

    [Fact]
    public async Task OnRequestAsync_SkipsNonMatchingRules()
    {
        var hook = new BreakpointHook { Enabled = true, TimeoutMs = 5000 };
        hook.AddRule("GET", "/api/users");

        var request = new InterceptedRequest
        {
            Method = "POST",
            Url = new Uri("http://example.com/api/orders"),
            CorrelationId = "test-6"
        };

        // Should pass through since it doesn't match the rule
        var result = await hook.OnRequestAsync(request);
        Assert.Same(request, result);
    }

    [Fact]
    public async Task OnRequestAsync_PausesMatchingRule()
    {
        var hook = new BreakpointHook { Enabled = true, TimeoutMs = 5000 };
        hook.AddRule("GET", "/api/users");

        var request = new InterceptedRequest
        {
            Method = "GET",
            Url = new Uri("http://example.com/api/users/123"),
            CorrelationId = "test-7"
        };

        var pauseTask = hook.OnRequestAsync(request);
        await Task.Delay(50);
        Assert.False(pauseTask.IsCompleted);

        hook.Release("test-7");
        var result = await pauseTask;
        Assert.Same(request, result);
    }

    [Fact]
    public void AddAndRemoveRules()
    {
        var hook = new BreakpointHook();
        var rule = hook.AddRule("GET", "/api/test");

        Assert.Single(hook.GetRules());
        Assert.True(hook.Enabled); // Auto-enabled

        hook.RemoveRule(rule.Id);
        Assert.Empty(hook.GetRules());
    }
}
