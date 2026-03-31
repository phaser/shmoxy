using shmoxy.frontend.models;
using shmoxy.frontend.services;
using Moq;
using Xunit;

namespace shmoxy.frontend.tests.services;

public class InspectionDataServiceWebSocketTests : IDisposable
{
    private readonly InspectionDataService _service;

    public InspectionDataServiceWebSocketTests()
    {
        var mockApiClient = new Mock<ApiClient>(Mock.Of<HttpClient>());
        _service = new InspectionDataService(mockApiClient.Object);
    }

    [Fact]
    public void ProcessEvent_WebSocketOpen_CreatesRowWithIsWebSocket()
    {
        var evt = new InspectionEventDto(
            Timestamp: DateTime.UtcNow,
            EventType: "websocket_open",
            Method: "GET",
            Url: "wss://example.com/ws",
            StatusCode: 101,
            CorrelationId: "ws-1",
            IsWebSocket: true);

        _service.ProcessEvent(evt);

        var rows = _service.GetRows();
        Assert.Single(rows);
        Assert.True(rows[0].IsWebSocket);
        Assert.Equal("wss://example.com/ws", rows[0].Url);
        Assert.Equal(101, rows[0].StatusCode);
    }

    [Fact]
    public void ProcessEvent_WebSocketMessage_AppendsFrameToRow()
    {
        _service.ProcessEvent(new InspectionEventDto(
            Timestamp: DateTime.UtcNow,
            EventType: "websocket_open",
            Method: "GET",
            Url: "wss://example.com/ws",
            StatusCode: 101,
            CorrelationId: "ws-2",
            IsWebSocket: true));

        var payload = System.Text.Encoding.UTF8.GetBytes("hello");
        _service.ProcessEvent(new InspectionEventDto(
            Timestamp: DateTime.UtcNow,
            EventType: "websocket_message",
            Method: "",
            Url: "",
            StatusCode: null,
            CorrelationId: "ws-2",
            Body: payload,
            FrameType: "text",
            Direction: "client",
            IsWebSocket: true));

        var rows = _service.GetRows();
        Assert.Single(rows);
        Assert.Single(rows[0].WebSocketFrames);
        Assert.Equal("client", rows[0].WebSocketFrames[0].Direction);
        Assert.Equal("text", rows[0].WebSocketFrames[0].FrameType);
        Assert.Equal("hello", rows[0].WebSocketFrames[0].Payload);
    }

    [Fact]
    public void ProcessEvent_WebSocketClose_MarksRowAsClosed()
    {
        var start = DateTime.UtcNow;
        _service.ProcessEvent(new InspectionEventDto(
            Timestamp: start,
            EventType: "websocket_open",
            Method: "GET",
            Url: "wss://example.com/ws",
            StatusCode: 101,
            CorrelationId: "ws-3",
            IsWebSocket: true));

        _service.ProcessEvent(new InspectionEventDto(
            Timestamp: start.AddSeconds(5),
            EventType: "websocket_close",
            Method: "",
            Url: "",
            StatusCode: null,
            CorrelationId: "ws-3",
            IsWebSocket: true));

        var rows = _service.GetRows();
        Assert.Single(rows);
        Assert.True(rows[0].WebSocketClosed);
        Assert.NotNull(rows[0].Duration);
    }

    [Fact]
    public void ProcessEvent_WebSocketMessage_UnknownCorrelationId_DoesNotCrash()
    {
        var payload = System.Text.Encoding.UTF8.GetBytes("orphan");
        _service.ProcessEvent(new InspectionEventDto(
            Timestamp: DateTime.UtcNow,
            EventType: "websocket_message",
            Method: "",
            Url: "",
            StatusCode: null,
            CorrelationId: "unknown-id",
            Body: payload,
            FrameType: "text",
            Direction: "server",
            IsWebSocket: true));

        var rows = _service.GetRows();
        Assert.Empty(rows);
    }

    public void Dispose()
    {
        _service.Dispose();
    }
}
