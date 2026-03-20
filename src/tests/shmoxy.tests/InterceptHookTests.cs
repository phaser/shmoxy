using shmoxy.models.dto;
using shmoxy.server.hooks;
using shmoxy.server.interfaces;

namespace shmoxy.tests;

public class InterceptHookTests
{
    [Fact]
    public void InterceptHookChain_ShouldExecuteHooksInOrder()
    {
        // Arrange
        var executionOrder = new List<string>();
        var chain = new InterceptHookChain();

        chain.Add(new TestHook("A", executionOrder));
        chain.Add(new TestHook("B", executionOrder));
        chain.Add(new TestHook("C", executionOrder));

        var request = new InterceptedRequest { Method = "GET" };

        // Act
        _ = chain.OnRequestAsync(request);

        // Assert - hooks should execute in order of addition
        Assert.Equal(new List<string> { "A", "B", "C" }, executionOrder);
    }

    [Fact]
    public void InterceptHookChain_ShouldStopOnCancel()
    {
        // Arrange
        var executionOrder = new List<string>();
        var chain = new InterceptHookChain();

        chain.Add(new TestHook("A", executionOrder));
        chain.Add(new TestHook("B", executionOrder, shouldCancel: true));
        chain.Add(new TestHook("C", executionOrder)); // Should not execute

        var request = new InterceptedRequest { Method = "GET" };

        // Act
        var result = _ = chain.OnRequestAsync(request);

        // Assert - hook C should not have executed
        Assert.Equal(new List<string> { "A" }, executionOrder);
    }

    [Fact]
    public async Task NoOpInterceptHook_ShouldPassThroughUnmodified()
    {
        // Arrange
        var hook = new NoOpInterceptHook();
        var request = new InterceptedRequest
        {
            Method = "POST",
            Path = "/test",
            Headers = { ["X-Custom"] = "value" }
        };

        // Act
        var result = await hook.OnRequestAsync(request);

        // Assert - should pass through unchanged
        Assert.NotNull(result);
        Assert.Equal("POST", result.Method);
        Assert.Equal("/test", result.Path);
    }

    private class TestHook(string name, List<string> executionOrder, bool shouldCancel = false) : IInterceptHook
    {
        public string Name => name;
        public bool ShouldCancel => shouldCancel;

        public Task<InterceptedRequest?> OnRequestAsync(InterceptedRequest request)
        {
            if (ShouldCancel) return Task.FromResult<InterceptedRequest?>(null);
            executionOrder.Add(Name);
            return Task.FromResult<InterceptedRequest?>(request);
        }

        public Task<InterceptedResponse?> OnResponseAsync(InterceptedResponse response) =>
            Task.FromResult<InterceptedResponse?>(response);
    }
}
