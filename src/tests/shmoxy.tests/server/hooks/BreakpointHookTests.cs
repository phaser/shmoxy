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

    [Fact]
    public async Task OnRequestAsync_EmptyCorrelationId_PassesThrough()
    {
        var hook = new BreakpointHook { Enabled = true };
        var request = new InterceptedRequest
        {
            Method = "GET",
            Url = new Uri("http://example.com"),
            CorrelationId = ""
        };

        var result = await hook.OnRequestAsync(request);
        Assert.Same(request, result);
    }

    [Fact]
    public async Task OnRequestAsync_NullCorrelationId_PassesThrough()
    {
        var hook = new BreakpointHook { Enabled = true };
        var request = new InterceptedRequest
        {
            Method = "GET",
            Url = new Uri("http://example.com"),
            CorrelationId = null!
        };

        var result = await hook.OnRequestAsync(request);
        Assert.Same(request, result);
    }

    [Fact]
    public void Release_NonExistentCorrelationId_ReturnsFalse()
    {
        var hook = new BreakpointHook { Enabled = true };

        var released = hook.Release("nonexistent");
        Assert.False(released);
    }

    [Fact]
    public void Drop_NonExistentCorrelationId_ReturnsFalse()
    {
        var hook = new BreakpointHook { Enabled = true };

        var dropped = hook.Drop("nonexistent");
        Assert.False(dropped);
    }

    [Fact]
    public void RemoveRule_NonExistentId_ReturnsFalse()
    {
        var hook = new BreakpointHook();

        var removed = hook.RemoveRule("nonexistent");
        Assert.False(removed);
    }

    [Fact]
    public void AddRule_AutoEnablesBreakpoints()
    {
        var hook = new BreakpointHook { Enabled = false };

        hook.AddRule("GET", "/api/test");

        Assert.True(hook.Enabled);
    }

    [Fact]
    public async Task OnRequestAsync_NoRules_BreaksOnAll()
    {
        // When enabled with no rules, should break on all requests (legacy behavior)
        var hook = new BreakpointHook { Enabled = true, TimeoutMs = 5000 };
        var request = new InterceptedRequest
        {
            Method = "DELETE",
            Url = new Uri("http://example.com/any/path"),
            CorrelationId = "test-norule"
        };

        var pauseTask = hook.OnRequestAsync(request);
        await Task.Delay(50);
        Assert.False(pauseTask.IsCompleted);

        hook.Release("test-norule");
        var result = await pauseTask;
        Assert.Same(request, result);
    }

    [Fact]
    public async Task OnRequestAsync_RuleWithNullMethod_MatchesAnyMethod()
    {
        var hook = new BreakpointHook { Enabled = true, TimeoutMs = 5000 };
        hook.AddRule(null, "/api/users");

        var request = new InterceptedRequest
        {
            Method = "DELETE",
            Url = new Uri("http://example.com/api/users/42"),
            CorrelationId = "test-nullmethod"
        };

        var pauseTask = hook.OnRequestAsync(request);
        await Task.Delay(50);
        Assert.False(pauseTask.IsCompleted);

        hook.Release("test-nullmethod");
        var result = await pauseTask;
        Assert.Same(request, result);
    }

    [Fact]
    public async Task OnRequestAsync_RuleMatchIsCaseInsensitive()
    {
        var hook = new BreakpointHook { Enabled = true, TimeoutMs = 5000 };
        hook.AddRule("get", "/API/USERS");

        var request = new InterceptedRequest
        {
            Method = "GET",
            Url = new Uri("http://example.com/api/users/123"),
            CorrelationId = "test-case"
        };

        var pauseTask = hook.OnRequestAsync(request);
        await Task.Delay(50);
        Assert.False(pauseTask.IsCompleted);

        hook.Release("test-case");
        await pauseTask;
    }

    [Fact]
    public async Task OnRequestAsync_ConcurrentPausesAreIndependent()
    {
        var hook = new BreakpointHook { Enabled = true, TimeoutMs = 5000 };

        var request1 = new InterceptedRequest
        {
            Method = "GET",
            Url = new Uri("http://example.com/1"),
            CorrelationId = "concurrent-1"
        };
        var request2 = new InterceptedRequest
        {
            Method = "POST",
            Url = new Uri("http://example.com/2"),
            CorrelationId = "concurrent-2"
        };

        var task1 = hook.OnRequestAsync(request1);
        var task2 = hook.OnRequestAsync(request2);

        await Task.Delay(50);
        Assert.Equal(2, hook.GetPausedRequests().Count);

        // Release only the first
        hook.Release("concurrent-1");
        var result1 = await task1;
        Assert.Same(request1, result1);

        // Second should still be paused
        Assert.False(task2.IsCompleted);
        Assert.Single(hook.GetPausedRequests());

        hook.Drop("concurrent-2");
        var result2 = await task2;
        Assert.Null(result2);
    }

    [Fact]
    public void GetPausedRequests_ReturnsSnapshot()
    {
        var hook = new BreakpointHook();
        var paused = hook.GetPausedRequests();
        Assert.Empty(paused);
    }

    [Fact]
    public async Task OnResponseAsync_AlwaysPassesThrough()
    {
        var hook = new BreakpointHook { Enabled = true };
        var response = new InterceptedResponse
        {
            StatusCode = 200,
            CorrelationId = "resp-1"
        };

        var result = await hook.OnResponseAsync(response);
        Assert.Same(response, result);
    }

    [Fact]
    public void AddRule_ReturnsRuleWithUniqueId()
    {
        var hook = new BreakpointHook();
        var rule1 = hook.AddRule("GET", "/api/test1");
        var rule2 = hook.AddRule("POST", "/api/test2");

        Assert.NotEqual(rule1.Id, rule2.Id);
        Assert.Equal(2, hook.GetRules().Count);
    }
}
