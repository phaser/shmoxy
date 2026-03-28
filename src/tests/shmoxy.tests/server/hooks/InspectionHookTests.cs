using shmoxy.models.dto;
using shmoxy.server.hooks;

namespace shmoxy.tests.server.hooks;

public class InspectionHookTests
{
    [Fact]
    public async Task OnRequestAsync_IncludesCorrelationId_InEvent()
    {
        var hook = new InspectionHook();
        hook.Enabled = true;

        var request = new InterceptedRequest
        {
            Method = "GET",
            Url = new Uri("https://example.com/test"),
            CorrelationId = "test-correlation-123"
        };

        await hook.OnRequestAsync(request);

        var reader = hook.GetReader();
        Assert.True(reader.TryRead(out var evt));
        Assert.Equal("request", evt.EventType);
        Assert.Equal("test-correlation-123", evt.CorrelationId);
    }

    [Fact]
    public async Task OnResponseAsync_IncludesCorrelationId_InEvent()
    {
        var hook = new InspectionHook();
        hook.Enabled = true;

        var response = new InterceptedResponse
        {
            StatusCode = 200,
            CorrelationId = "test-correlation-456"
        };

        await hook.OnResponseAsync(response);

        var reader = hook.GetReader();
        Assert.True(reader.TryRead(out var evt));
        Assert.Equal("response", evt.EventType);
        Assert.Equal("test-correlation-456", evt.CorrelationId);
    }

    [Fact]
    public async Task RequestAndResponse_ShareCorrelationId()
    {
        var hook = new InspectionHook();
        hook.Enabled = true;

        var correlationId = Guid.NewGuid().ToString();

        await hook.OnRequestAsync(new InterceptedRequest
        {
            Method = "POST",
            Url = new Uri("https://example.com/api"),
            CorrelationId = correlationId
        });

        await hook.OnResponseAsync(new InterceptedResponse
        {
            StatusCode = 201,
            CorrelationId = correlationId
        });

        var reader = hook.GetReader();
        Assert.True(reader.TryRead(out var requestEvt));
        Assert.True(reader.TryRead(out var responseEvt));
        Assert.Equal(requestEvt.CorrelationId, responseEvt.CorrelationId);
        Assert.Equal(correlationId, requestEvt.CorrelationId);
    }

    [Fact]
    public async Task OnRequestAsync_WhenDisabled_DoesNotWriteEvent()
    {
        var hook = new InspectionHook();
        hook.Enabled = false;

        await hook.OnRequestAsync(new InterceptedRequest
        {
            Method = "GET",
            Url = new Uri("https://example.com"),
            CorrelationId = "should-not-appear"
        });

        var reader = hook.GetReader();
        Assert.False(reader.TryRead(out _));
    }

    [Fact]
    public async Task OnPassthroughAsync_WhenEnabled_WritesPassthroughEvent()
    {
        var hook = new InspectionHook();
        hook.Enabled = true;

        await hook.OnPassthroughAsync("example.com", 443);

        var reader = hook.GetReader();
        Assert.True(reader.TryRead(out var evt));
        Assert.Equal("passthrough", evt.EventType);
        Assert.Equal("CONNECT", evt.Method);
        Assert.Equal("https://example.com:443", evt.Url);
        Assert.NotNull(evt.CorrelationId);
    }

    [Fact]
    public async Task OnPassthroughAsync_WhenDisabled_DoesNotWriteEvent()
    {
        var hook = new InspectionHook();
        hook.Enabled = false;

        await hook.OnPassthroughAsync("example.com", 443);

        var reader = hook.GetReader();
        Assert.False(reader.TryRead(out _));
    }
}
