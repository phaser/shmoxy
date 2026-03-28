using System.Text;
using shmoxy.frontend.models;

namespace shmoxy.frontend.services;

public class InspectionDataService : IDisposable
{
    private readonly ApiClient _apiClient;
    private readonly List<InspectionRow> _rows = new();
    private readonly Queue<(int RowIndex, DateTime Timestamp)> _unpairedRequests = new();
    private readonly object _lock = new();

    private CancellationTokenSource? _cts;
    private Task? _streamTask;
    private int _nextId = 1;
    private bool _disposed;

    public const int MaxRows = 1000;

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
            _unpairedRequests.Clear();
            _nextId = 1;
        }
        OnRowsChanged?.Invoke();
    }

    public void LoadRows(IReadOnlyList<InspectionRow> rows)
    {
        lock (_lock)
        {
            _rows.Clear();
            _unpairedRequests.Clear();
            _nextId = 1;

            foreach (var row in rows)
            {
                row.Id = _nextId++;
                _rows.Add(row);
            }
        }
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

    private void ProcessEvent(InspectionEventDto evt)
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
            _unpairedRequests.Enqueue((_rows.Count - 1, evt.Timestamp));

            if (_rows.Count > MaxRows)
            {
                _rows.RemoveAt(0);
                var adjusted = new Queue<(int, DateTime)>();
                while (_unpairedRequests.Count > 0)
                {
                    var (idx, ts) = _unpairedRequests.Dequeue();
                    if (idx > 0)
                        adjusted.Enqueue((idx - 1, ts));
                }
                while (adjusted.Count > 0)
                    _unpairedRequests.Enqueue(adjusted.Dequeue());
            }
        }
        else if (string.Equals(evt.EventType, "response", StringComparison.OrdinalIgnoreCase))
        {
            if (_unpairedRequests.Count > 0)
            {
                var (rowIndex, requestTimestamp) = _unpairedRequests.Dequeue();
                if (rowIndex < _rows.Count)
                {
                    _rows[rowIndex].Duration = evt.Timestamp - requestTimestamp;
                    _rows[rowIndex].StatusCode = evt.StatusCode;
                    _rows[rowIndex].ResponseHeaders = evt.Headers ?? new Dictionary<string, string>();
                    _rows[rowIndex].ResponseBody = DecodeBody(evt.Body);
                }
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
}
