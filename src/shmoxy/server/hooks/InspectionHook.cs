using System.Threading.Channels;
using shmoxy.models.dto;
using shmoxy.server.interfaces;
using shmoxy.shared.ipc;

namespace shmoxy.server.hooks;

/// <summary>
/// Hook that captures intercepted requests and responses for inspection.
/// Off by default - no performance overhead when disabled.
/// </summary>
public class InspectionHook : IInterceptHook, IDisposable
{
    private readonly Channel<InspectionEvent> _channel;
    private readonly ChannelReader<InspectionEvent> _reader;
    private bool _enabled;
    private bool _disposed;

    public InspectionHook()
    {
        _channel = Channel.CreateUnbounded<InspectionEvent>();
        _reader = _channel.Reader;
        _enabled = false;
    }

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public ChannelReader<InspectionEvent> GetReader() => _reader;

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
            CorrelationId = request.CorrelationId
        };

        _channel.Writer.TryWrite(evt);
        return Task.FromResult<InterceptedRequest?>(request);
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
            CorrelationId = response.CorrelationId
        };

        _channel.Writer.TryWrite(evt);
        return Task.FromResult<InterceptedResponse?>(response);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _channel.Writer.Complete();
        _disposed = true;
    }
}
