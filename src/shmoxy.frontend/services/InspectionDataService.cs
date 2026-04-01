using System.Text;
using shmoxy.frontend.models;

namespace shmoxy.frontend.services;

public class InspectionDataService : IDisposable
{
    private readonly ApiClient _apiClient;
    private readonly List<InspectionRow> _rows = new();
    private readonly Dictionary<string, (int RowIndex, DateTime Timestamp)> _pendingRequests = new();
    private readonly object _lock = new();

    private CancellationTokenSource? _cts;
    private Task? _streamTask;
    private int _nextId = 1;
    private bool _disposed;

    public const int MaxRows = 1000;

    public bool IsCapturing => _streamTask is not null && !_streamTask.IsCompleted;

    public string? ActiveSessionId { get; private set; }
    public string? ActiveSessionName { get; private set; }

    // Filter state persisted across navigation (lives in the singleton, survives component disposal)
    public string SearchQuery { get; set; } = "";
    public string MethodFilter { get; set; } = "";
    public string ProtocolFilter { get; set; } = "all";
    public bool ApiOnlyFilter { get; set; }
    public HashSet<string> SelectedDomains { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool AllDomainsSelected { get; set; } = true;

    public event Action? OnRowsChanged;

    public InspectionDataService(ApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public IReadOnlyList<InspectionRow> GetRows()
    {
        lock (_lock)
        {
            return _rows.ToList();
        }
    }

    public void StartCapture()
    {
        if (_streamTask is not null && !_streamTask.IsCompleted)
            return;

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _streamTask = ConsumeStreamAsync(_cts.Token);
    }

    public void StopCapture()
    {
        _cts?.Cancel();
    }

    public void Clear()
    {
        lock (_lock)
        {
            _rows.Clear();
            _pendingRequests.Clear();
            _nextId = 1;
        }
        ActiveSessionId = null;
        ActiveSessionName = null;
        OnRowsChanged?.Invoke();
    }

    public void LoadRows(IReadOnlyList<InspectionRow> rows, string? sessionId = null, string? sessionName = null)
    {
        lock (_lock)
        {
            _rows.Clear();
            _pendingRequests.Clear();
            _nextId = 1;

            foreach (var row in rows)
            {
                row.Id = _nextId++;
                row.Origin = RowOrigin.Loaded;
                _rows.Add(row);
            }
        }
        ActiveSessionId = sessionId;
        ActiveSessionName = sessionName;
        OnRowsChanged?.Invoke();
    }

    private async Task ConsumeStreamAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var evt in _apiClient.StreamInspectionEventsAsync("local", ct))
            {
                lock (_lock)
                {
                    ProcessEvent(evt);
                }
                OnRowsChanged?.Invoke();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on stop/dispose
        }
        catch (Exception)
        {
            // Stream ended (proxy stopped, connection lost)
        }
    }

    internal void ProcessEvent(InspectionEventDto evt)
    {
        if (string.Equals(evt.EventType, "request", StringComparison.OrdinalIgnoreCase))
        {
            var row = new InspectionRow
            {
                Id = _nextId++,
                Method = evt.Method,
                Url = evt.Url,
                Timestamp = evt.Timestamp,
                RequestHeaders = evt.Headers ?? new Dictionary<string, string>(),
                RequestBody = DecodeBody(evt.Body)
            };
            _rows.Add(row);

            if (!string.IsNullOrEmpty(evt.CorrelationId))
            {
                _pendingRequests[evt.CorrelationId] = (_rows.Count - 1, evt.Timestamp);
            }

            if (_rows.Count > MaxRows)
            {
                _rows.RemoveAt(0);
                var keysToRemove = new List<string>();
                var updated = new Dictionary<string, (int, DateTime)>();
                foreach (var (key, (idx, ts)) in _pendingRequests)
                {
                    if (idx > 0)
                        updated[key] = (idx - 1, ts);
                    else
                        keysToRemove.Add(key);
                }
                foreach (var key in keysToRemove)
                    _pendingRequests.Remove(key);
                foreach (var (key, value) in updated)
                    _pendingRequests[key] = value;
            }
        }
        else if (string.Equals(evt.EventType, "passthrough", StringComparison.OrdinalIgnoreCase))
        {
            var row = new InspectionRow
            {
                Id = _nextId++,
                Method = evt.Method,
                Url = evt.Url,
                Timestamp = evt.Timestamp,
                IsPassthrough = true
            };
            _rows.Add(row);

            if (_rows.Count > MaxRows)
            {
                _rows.RemoveAt(0);
            }
        }
        else if (string.Equals(evt.EventType, "response", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(evt.CorrelationId) && _pendingRequests.Remove(evt.CorrelationId, out var pending))
            {
                var (rowIndex, requestTimestamp) = pending;
                if (rowIndex < _rows.Count)
                {
                    _rows[rowIndex].Duration = evt.Timestamp - requestTimestamp;
                    _rows[rowIndex].StatusCode = evt.StatusCode;
                    _rows[rowIndex].ResponseHeaders = evt.Headers ?? new Dictionary<string, string>();
                    _rows[rowIndex].ResponseBody = DecodeBody(evt.Body);
                }
            }
        }
        else if (string.Equals(evt.EventType, "websocket_open", StringComparison.OrdinalIgnoreCase))
        {
            var row = new InspectionRow
            {
                Id = _nextId++,
                Method = evt.Method,
                Url = evt.Url,
                Timestamp = evt.Timestamp,
                StatusCode = evt.StatusCode,
                IsWebSocket = true,
                RequestHeaders = evt.Headers ?? new Dictionary<string, string>()
            };
            _rows.Add(row);

            if (!string.IsNullOrEmpty(evt.CorrelationId))
            {
                _pendingRequests[evt.CorrelationId] = (_rows.Count - 1, evt.Timestamp);
            }

            if (_rows.Count > MaxRows)
                _rows.RemoveAt(0);
        }
        else if (string.Equals(evt.EventType, "websocket_message", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(evt.CorrelationId) &&
                _pendingRequests.TryGetValue(evt.CorrelationId, out var pending))
            {
                var (rowIndex, _) = pending;
                if (rowIndex < _rows.Count)
                {
                    _rows[rowIndex].WebSocketFrames.Add(new WebSocketFrameInfo
                    {
                        Timestamp = evt.Timestamp,
                        Direction = evt.Direction ?? "unknown",
                        FrameType = evt.FrameType ?? "unknown",
                        Payload = DecodeBody(evt.Body)
                    });
                }
            }
        }
        else if (string.Equals(evt.EventType, "websocket_close", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(evt.CorrelationId) &&
                _pendingRequests.TryGetValue(evt.CorrelationId, out var pending))
            {
                var (rowIndex, requestTimestamp) = pending;
                if (rowIndex < _rows.Count)
                {
                    _rows[rowIndex].WebSocketClosed = true;
                    _rows[rowIndex].Duration = evt.Timestamp - requestTimestamp;
                }
                _pendingRequests.Remove(evt.CorrelationId);
            }
        }
    }

    private static string? DecodeBody(byte[]? body)
    {
        if (body is null || body.Length == 0)
            return null;

        try
        {
            return Encoding.UTF8.GetString(body);
        }
        catch
        {
            return $"[Binary data: {body.Length} bytes]";
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _cts?.Cancel();
        _cts?.Dispose();
        _disposed = true;
    }
}

public enum RowOrigin
{
    Live,
    Loaded
}

public class InspectionRow
{
    public int Id { get; set; }
    public string Method { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public TimeSpan? Duration { get; set; }
    public DateTime Timestamp { get; set; }
    public int? StatusCode { get; set; }
    public Dictionary<string, string> RequestHeaders { get; set; } = new();
    public Dictionary<string, string> ResponseHeaders { get; set; } = new();
    public string? RequestBody { get; set; }
    public string? ResponseBody { get; set; }
    public RowOrigin Origin { get; set; } = RowOrigin.Live;
    public bool IsPassthrough { get; set; }
    public bool IsWebSocket { get; set; }
    public List<WebSocketFrameInfo> WebSocketFrames { get; set; } = new();
    public bool WebSocketClosed { get; set; }
}
