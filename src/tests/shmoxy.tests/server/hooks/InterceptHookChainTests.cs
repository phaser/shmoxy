using shmoxy.models.dto;
using shmoxy.server.hooks;
using shmoxy.server.interfaces;

namespace shmoxy.tests.server.hooks;

public class InterceptHookChainTests
{
    [Fact]
    public async Task OnPassthroughAsync_ForwardsToAllHooks()
    {
        var hook1 = new TestPassthroughHook();
        var hook2 = new TestPassthroughHook();
        var chain = new InterceptHookChain().Add(hook1).Add(hook2);

        await chain.OnPassthroughAsync("example.com", 443);

        Assert.Equal("example.com", hook1.LastHost);
        Assert.Equal(443, hook1.LastPort);
        Assert.Equal("example.com", hook2.LastHost);
        Assert.Equal(443, hook2.LastPort);
    }

    [Fact]
    public async Task OnPassthroughAsync_NoHooks_DoesNotThrow()
    {
        var chain = new InterceptHookChain();

        await chain.OnPassthroughAsync("example.com", 443);
    }

    private class TestPassthroughHook : IInterceptHook
    {
        public string? LastHost { get; private set; }
        public int? LastPort { get; private set; }

        public Task<InterceptedRequest?> OnRequestAsync(InterceptedRequest request)
            => Task.FromResult<InterceptedRequest?>(request);

        public Task<InterceptedResponse?> OnResponseAsync(InterceptedResponse response)
            => Task.FromResult<InterceptedResponse?>(response);

        public Task OnPassthroughAsync(string host, int port)
        {
            LastHost = host;
            LastPort = port;
            return Task.CompletedTask;
        }
    }
}
