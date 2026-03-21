namespace shmoxy.api.models;

public enum ProxyProcessState
{
    Starting,
    Running,
    Stopping,
    Stopped,
    Crashed
}

public record ProxyInstanceState
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public ProxyProcessState State { get; init; }
    public int? ProcessId { get; init; }
    public string? SocketPath { get; init; }
    public int? Port { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? StoppedAt { get; init; }
    public string? ExitReason { get; init; }
}
