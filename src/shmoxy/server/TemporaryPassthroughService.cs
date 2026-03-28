using shmoxy.shared.ipc;

namespace shmoxy.server;

/// <summary>
/// Manages temporary TLS passthrough windows for domains where detectors
/// have identified TLS fingerprint rejections. Entries auto-expire after
/// a configurable number of connections or a timeout.
/// </summary>
public class TemporaryPassthroughService : IDisposable
{
    private readonly Dictionary<string, Entry> _entries = new();
    private readonly object _lock = new();
    private readonly int _maxConnections;
    private readonly TimeSpan _timeout;
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    public event Action<TemporaryPassthroughEntry>? OnActivated;
    public event Action<string>? OnExpired;

    public TemporaryPassthroughService(int maxConnections = 2, TimeSpan? timeout = null)
    {
        _maxConnections = maxConnections;
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
        _cleanupTimer = new Timer(Cleanup, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Activates temporary passthrough for a domain. If already active, resets the window.
    /// </summary>
    public void Activate(string host, string detectorId, string reason)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            _entries[host] = new Entry
            {
                Host = host,
                DetectorId = detectorId,
                Reason = reason,
                RemainingConnections = _maxConnections,
                MaxConnections = _maxConnections,
                ActivatedAt = now,
                ExpiresAt = now + _timeout
            };
        }

        OnActivated?.Invoke(ToDto(host));
    }

    /// <summary>
    /// Checks whether a domain has an active temporary passthrough.
    /// </summary>
    public bool ShouldPassthrough(string host)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(host, out var entry))
                return false;

            if (entry.RemainingConnections <= 0 || DateTime.UtcNow >= entry.ExpiresAt)
            {
                ExpireEntry(host);
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Records that a temporary passthrough connection was made, decrementing the counter.
    /// </summary>
    public void RecordConnection(string host)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(host, out var entry))
                return;

            entry.RemainingConnections--;

            if (entry.RemainingConnections <= 0)
                ExpireEntry(host);
        }
    }

    /// <summary>
    /// Gets all currently active temporary passthrough entries.
    /// </summary>
    public IReadOnlyList<TemporaryPassthroughEntry> GetActiveEntries()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            return _entries.Values
                .Where(e => e.RemainingConnections > 0 && now < e.ExpiresAt)
                .Select(e => new TemporaryPassthroughEntry
                {
                    Host = e.Host,
                    DetectorId = e.DetectorId,
                    Reason = e.Reason,
                    RemainingConnections = e.RemainingConnections,
                    MaxConnections = e.MaxConnections,
                    ActivatedAt = e.ActivatedAt,
                    ExpiresAt = e.ExpiresAt
                })
                .ToList();
        }
    }

    private void ExpireEntry(string host)
    {
        // Must be called under _lock
        _entries.Remove(host);
        OnExpired?.Invoke(host);
    }

    private TemporaryPassthroughEntry ToDto(string host)
    {
        lock (_lock)
        {
            var entry = _entries[host];
            return new TemporaryPassthroughEntry
            {
                Host = entry.Host,
                DetectorId = entry.DetectorId,
                Reason = entry.Reason,
                RemainingConnections = entry.RemainingConnections,
                MaxConnections = entry.MaxConnections,
                ActivatedAt = entry.ActivatedAt,
                ExpiresAt = entry.ExpiresAt
            };
        }
    }

    private void Cleanup(object? state)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var expired = _entries.Where(kv =>
                kv.Value.RemainingConnections <= 0 || now >= kv.Value.ExpiresAt)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var host in expired)
                ExpireEntry(host);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cleanupTimer.Dispose();
    }

    private class Entry
    {
        public required string Host { get; init; }
        public required string DetectorId { get; init; }
        public required string Reason { get; init; }
        public int RemainingConnections { get; set; }
        public required int MaxConnections { get; init; }
        public required DateTime ActivatedAt { get; init; }
        public required DateTime ExpiresAt { get; init; }
    }
}
