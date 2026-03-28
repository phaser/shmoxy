namespace shmoxy.shared.ipc;

/// <summary>
/// Status information returned by the proxy's IPC /ipc/status endpoint.
/// </summary>
public record ProxyStatus
{
    public bool IsListening { get; init; }
    public int Port { get; init; }
    public TimeSpan Uptime { get; init; }
    public int ActiveConnections { get; init; }
    public string? Version { get; init; }
}
