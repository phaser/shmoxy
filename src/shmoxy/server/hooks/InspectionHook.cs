using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;
using shmoxy.models;
using shmoxy.models.dto;
using shmoxy.server.interfaces;
using shmoxy.shared.ipc;

namespace shmoxy.server.hooks;

/// <summary>
/// Hook that captures intercepted requests and responses for inspection.
/// Enabled by default so traffic is captured from application startup.
/// Uses a bounded channel to prevent unbounded memory growth when no consumer is connected.
/// </summary>
public class InspectionHook : IInterceptHook, IDisposable
{
    public const int MaxChannelCapacity = 10_000;

    private readonly Channel<InspectionEvent> _channel;
    private readonly ChannelReader<InspectionEvent> _reader;
    private readonly ConcurrentDictionary<string, byte> _streamingRequests = new();
    private bool _enabled;
    private bool _disposed;

    public InspectionHook()
    {
        _channel = Channel.CreateBounded<InspectionEvent>(new BoundedChannelOptions(MaxChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });
        _reader = _channel.Reader;
        _enabled = true;
    }

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public ChannelReader<InspectionEvent> GetReader() => _reader;

    public bool RequiresRequestBodyCapture(InterceptedRequest request) => _enabled && !_disposed;

    public bool RequiresResponseBodyCapture(InterceptedResponse response) => _enabled && !_disposed;

    public Task<InterceptedRequest?> OnRequestAsync(InterceptedRequest request)
    {
        if (!_enabled || _disposed)
            return Task.FromResult<InterceptedRequest?>(request);

        var evt = new InspectionEvent
        {
            Timestamp = DateTime.UtcNow,
            EventType = "request",
            Method = request.Method,
            Url = request.Url?.ToString() ?? string.Empty,
            Headers = request.Headers,
            Body = request.Body,
            BodyLength = request.BodyLength,
            BodyTruncated = request.BodyTruncated,
            ContentEncoding = request.ContentEncoding,
            CorrelationId = request.CorrelationId
        };

        if (request.BodyLength == null && request.CorrelationId != null)
            _streamingRequests.TryAdd(request.CorrelationId, 0);

        _channel.Writer.TryWrite(evt);
        return Task.FromResult<InterceptedRequest?>(request);
    }

    public Task OnRequestBodyTransferredAsync(InterceptedRequest request)
    {
        if (_disposed)
            return Task.CompletedTask;

        if (request.CorrelationId == null ||
            !_streamingRequests.TryRemove(request.CorrelationId, out _))
        {
            return Task.CompletedTask;
        }

        if (!_enabled)
            return Task.CompletedTask;

        var evt = new InspectionEvent
        {
            Timestamp = DateTime.UtcNow,
            EventType = "request_body",
            Headers = request.Headers,
            Body = request.Body,
            BodyLength = request.BodyLength,
            BodyTruncated = request.BodyTruncated,
            ContentEncoding = request.ContentEncoding,
            CorrelationId = request.CorrelationId
        };

        _channel.Writer.TryWrite(evt);
        return Task.CompletedTask;
    }

    public Task<InterceptedResponse?> OnResponseAsync(InterceptedResponse response)
    {
        if (!_enabled || _disposed)
            return Task.FromResult<InterceptedResponse?>(response);

        var evt = new InspectionEvent
        {
            Timestamp = DateTime.UtcNow,
            EventType = "response",
            Method = string.Empty,
            Url = string.Empty,
            StatusCode = response.StatusCode,
            Headers = response.Headers,
            Body = response.Body,
            BodyLength = response.BodyLength,
            BodyTruncated = response.BodyTruncated,
            ContentEncoding = response.ContentEncoding,
            CorrelationId = response.CorrelationId,
            Timing = response.Timing
        };

        _channel.Writer.TryWrite(evt);
        return Task.FromResult<InterceptedResponse?>(response);
    }

    public Task OnPassthroughAsync(string host, int port)
    {
        if (!_enabled || _disposed)
            return Task.CompletedTask;

        var evt = new InspectionEvent
        {
            Timestamp = DateTime.UtcNow,
            EventType = "passthrough",
            Method = "CONNECT",
            Url = $"https://{host}:{port}",
            CorrelationId = Guid.NewGuid().ToString()
        };

        _channel.Writer.TryWrite(evt);
        return Task.CompletedTask;
    }

    private const int MaxBodyBytes = 1_048_576; // 1 MB

    public Task OnWebSocketOpenAsync(string host, string path, string correlationId)
    {
        if (!_enabled || _disposed)
            return Task.CompletedTask;

        var evt = new InspectionEvent
        {
            Timestamp = DateTime.UtcNow,
            EventType = "websocket_open",
            Method = "GET",
            Url = $"wss://{host}{path}",
            StatusCode = 101,
            CorrelationId = correlationId,
            IsWebSocket = true
        };

        _channel.Writer.TryWrite(evt);
        return Task.CompletedTask;
    }

    public Task OnWebSocketFrameAsync(string correlationId, WebSocketFrame frame, string direction)
    {
        if (!_enabled || _disposed)
            return Task.CompletedTask;

        var payload = frame.Payload.Length > MaxBodyBytes
            ? frame.Payload[..MaxBodyBytes]
            : frame.Payload;

        var evt = new InspectionEvent
        {
            Timestamp = DateTime.UtcNow,
            EventType = "websocket_message",
            CorrelationId = correlationId,
            Body = payload,
            FrameType = frame.Opcode.ToString(),
            Direction = direction,
            IsWebSocket = true
        };

        _channel.Writer.TryWrite(evt);
        return Task.CompletedTask;
    }

    public Task OnWebSocketCloseAsync(string correlationId, string? reason)
    {
        if (!_enabled || _disposed)
            return Task.CompletedTask;

        var evt = new InspectionEvent
        {
            Timestamp = DateTime.UtcNow,
            EventType = "websocket_close",
            CorrelationId = correlationId,
            IsWebSocket = true,
            Body = reason != null ? Encoding.UTF8.GetBytes(reason) : null
        };

        _channel.Writer.TryWrite(evt);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _channel.Writer.Complete();
        _disposed = true;
    }
}
