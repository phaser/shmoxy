using System.Collections.Concurrent;
using shmoxy.shared.ipc;

namespace shmoxy.server;

/// <summary>
/// Thread-safe in-memory buffer for session log entries.
/// Captures proxy activity (hook events, detector triggers, errors)
/// so logs can be persisted alongside sessions.
/// </summary>
public class SessionLogBuffer
{
    private readonly ConcurrentQueue<SessionLogEntry> _entries = new();
    private bool _enabled;

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    /// <summary>
    /// Add a log entry if logging is enabled.
    /// </summary>
    public void Log(string level, string category, string message)
    {
        if (!_enabled)
            return;

        _entries.Enqueue(new SessionLogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = level,
            Category = category,
            Message = message
        });
    }

    public void Info(string category, string message) => Log("Info", category, message);
    public void Warn(string category, string message) => Log("Warn", category, message);
    public void Error(string category, string message) => Log("Error", category, message);

    /// <summary>
    /// Drain all buffered entries and return them. Clears the buffer.
    /// </summary>
    public List<SessionLogEntry> Drain()
    {
        var entries = new List<SessionLogEntry>();
        while (_entries.TryDequeue(out var entry))
            entries.Add(entry);
        return entries;
    }

    /// <summary>
    /// Get a snapshot of all buffered entries without draining.
    /// </summary>
    public List<SessionLogEntry> Snapshot()
    {
        return _entries.ToList();
    }
}
