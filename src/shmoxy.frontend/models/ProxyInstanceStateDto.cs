namespace shmoxy.frontend.models;

public record ProxyInstanceStateDto(
    string Id,
    string State,
    int? ProcessId,
    string? SocketPath,
    int? Port,
    DateTime? StartedAt,
    DateTime? StoppedAt,
    string? ExitReason
);
