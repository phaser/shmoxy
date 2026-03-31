using shmoxy.models;
using shmoxy.server.hooks;

namespace shmoxy.tests.server.hooks;

public class InspectionHookWebSocketTests
{
    [Fact]
    public async Task OnWebSocketOpenAsync_WhenEnabled_EmitsCorrectEvent()
    {
        var hook = new InspectionHook();
        hook.Enabled = true;

        await hook.OnWebSocketOpenAsync("example.com", "/ws/chat", "ws-corr-1");

        var reader = hook.GetReader();
        Assert.True(reader.TryRead(out var evt));
        Assert.Equal("websocket_open", evt.EventType);
        Assert.Equal("GET", evt.Method);
        Assert.Equal("wss://example.com/ws/chat", evt.Url);
        Assert.Equal(101, evt.StatusCode);
        Assert.Equal("ws-corr-1", evt.CorrelationId);
        Assert.True(evt.IsWebSocket);
    }

    [Fact]
    public async Task OnWebSocketFrameAsync_TextFrame_EmitsCorrectEvent()
    {
        var hook = new InspectionHook();
        hook.Enabled = true;

        var frame = new WebSocketFrame
        {
            Fin = true,
            Opcode = WebSocketOpcode.Text,
            Payload = "hello"u8.ToArray()
        };

        await hook.OnWebSocketFrameAsync("ws-corr-2", frame, "client");

        var reader = hook.GetReader();
        Assert.True(reader.TryRead(out var evt));
        Assert.Equal("websocket_message", evt.EventType);
        Assert.Equal("ws-corr-2", evt.CorrelationId);
        Assert.Equal("hello"u8.ToArray(), evt.Body);
        Assert.Equal("Text", evt.FrameType);
        Assert.Equal("client", evt.Direction);
        Assert.True(evt.IsWebSocket);
    }

    [Fact]
    public async Task OnWebSocketCloseAsync_EmitsCorrectEvent()
    {
        var hook = new InspectionHook();
        hook.Enabled = true;

        await hook.OnWebSocketCloseAsync("ws-corr-3", "normal closure");

        var reader = hook.GetReader();
        Assert.True(reader.TryRead(out var evt));
        Assert.Equal("websocket_close", evt.EventType);
        Assert.Equal("ws-corr-3", evt.CorrelationId);
        Assert.True(evt.IsWebSocket);
        Assert.NotNull(evt.Body);
        Assert.Equal("normal closure", System.Text.Encoding.UTF8.GetString(evt.Body));
    }

    [Fact]
    public async Task OnWebSocketOpenAsync_WhenDisabled_DoesNotEmitEvent()
    {
        var hook = new InspectionHook();
        hook.Enabled = false;

        await hook.OnWebSocketOpenAsync("example.com", "/ws", "ws-corr-4");

        var reader = hook.GetReader();
        Assert.False(reader.TryRead(out _));
    }

    [Fact]
    public async Task OnWebSocketFrameAsync_WhenDisabled_DoesNotEmitEvent()
    {
        var hook = new InspectionHook();
        hook.Enabled = false;

        var frame = new WebSocketFrame
        {
            Fin = true,
            Opcode = WebSocketOpcode.Text,
            Payload = "hello"u8.ToArray()
        };

        await hook.OnWebSocketFrameAsync("ws-corr-5", frame, "server");

        var reader = hook.GetReader();
        Assert.False(reader.TryRead(out _));
    }

    [Fact]
    public async Task OnWebSocketCloseAsync_WhenDisabled_DoesNotEmitEvent()
    {
        var hook = new InspectionHook();
        hook.Enabled = false;

        await hook.OnWebSocketCloseAsync("ws-corr-6", "going away");

        var reader = hook.GetReader();
        Assert.False(reader.TryRead(out _));
    }
}
