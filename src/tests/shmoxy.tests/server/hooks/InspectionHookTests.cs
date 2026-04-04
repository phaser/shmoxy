using shmoxy.models.dto;
using shmoxy.server.hooks;
using shmoxy.shared.ipc;

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
    public void Enabled_DefaultsToTrue()
    {
        var hook = new InspectionHook();
        Assert.True(hook.Enabled);
    }

    [Fact]
    public async Task OnRequestAsync_CapturesWithoutExplicitEnable()
    {
        var hook = new InspectionHook();
        // Do NOT set Enabled — it should default to true

        await hook.OnRequestAsync(new InterceptedRequest
        {
            Method = "GET",
            Url = new Uri("https://example.com/auto"),
            CorrelationId = "auto-capture"
        });

        var reader = hook.GetReader();
        Assert.True(reader.TryRead(out var evt));
        Assert.Equal("auto-capture", evt.CorrelationId);
    }

    [Fact]
    public async Task BoundedChannel_DropsOldestWhenFull()
    {
        var hook = new InspectionHook();

        // Write more events than the channel capacity
        for (var i = 0; i < InspectionHook.MaxChannelCapacity + 100; i++)
        {
            await hook.OnRequestAsync(new InterceptedRequest
            {
                Method = "GET",
                Url = new Uri($"https://example.com/{i}"),
                CorrelationId = $"evt-{i}"
            });
        }

        // Should have exactly MaxChannelCapacity events (oldest were dropped)
        var reader = hook.GetReader();
        var count = 0;
        while (reader.TryRead(out _))
            count++;

        Assert.Equal(InspectionHook.MaxChannelCapacity, count);
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

    [Fact]
    public async Task OnResponseAsync_IncludesTiming_InEvent()
    {
        var hook = new InspectionHook();
        var timing = new TimingInfo
        {
            ConnectMs = 10.5,
            TlsMs = 25.3,
            SendMs = 1.2,
            WaitMs = 50.0,
            ReceiveMs = 15.7,
            Reused = false
        };

        var response = new InterceptedResponse
        {
            StatusCode = 200,
            CorrelationId = "timing-test",
            Timing = timing
        };

        await hook.OnResponseAsync(response);

        var reader = hook.GetReader();
        Assert.True(reader.TryRead(out var evt));
        Assert.NotNull(evt.Timing);
        Assert.Equal(10.5, evt.Timing.ConnectMs);
        Assert.Equal(25.3, evt.Timing.TlsMs);
        Assert.Equal(1.2, evt.Timing.SendMs);
        Assert.Equal(50.0, evt.Timing.WaitMs);
        Assert.Equal(15.7, evt.Timing.ReceiveMs);
        Assert.False(evt.Timing.Reused);
    }

    [Fact]
    public async Task OnResponseAsync_NullTiming_PropagatesNull()
    {
        var hook = new InspectionHook();

        var response = new InterceptedResponse
        {
            StatusCode = 200,
            CorrelationId = "no-timing",
            Timing = null
        };

        await hook.OnResponseAsync(response);

        var reader = hook.GetReader();
        Assert.True(reader.TryRead(out var evt));
        Assert.Null(evt.Timing);
    }

    [Fact]
    public async Task OnResponseAsync_ReusedConnection_TimingHasNullConnectAndTls()
    {
        var hook = new InspectionHook();
        var timing = new TimingInfo
        {
            ConnectMs = null,
            TlsMs = null,
            SendMs = 0.5,
            WaitMs = 30.0,
            ReceiveMs = 10.0,
            Reused = true
        };

        var response = new InterceptedResponse
        {
            StatusCode = 200,
            CorrelationId = "reused-conn",
            Timing = timing
        };

        await hook.OnResponseAsync(response);

        var reader = hook.GetReader();
        Assert.True(reader.TryRead(out var evt));
        Assert.NotNull(evt.Timing);
        Assert.Null(evt.Timing.ConnectMs);
        Assert.Null(evt.Timing.TlsMs);
        Assert.True(evt.Timing.Reused);
    }
}
