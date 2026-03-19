namespace shmoxy.shared.ipc;

/// <summary>
/// Request/response DTOs for IPC commands.
/// </summary>
public record ShutdownRequest;

public record ShutdownResponse
{
    public bool Success { get; init; }
    public string? Message { get; init; }
}

public record EnableHookRequest
{
    public string HookId { get; init; } = string.Empty;
}

public record EnableHookResponse
{
    public bool Success { get; init; }
    public string? Message { get; init; }
}

public record DisableHookRequest
{
    public string HookId { get; init; } = string.Empty;
}

public record DisableHookResponse
{
    public bool Success { get; init; }
    public string? Message { get; init; }
}

public record EnableInspectionRequest;

public record EnableInspectionResponse
{
    public bool Success { get; init; }
    public string? Message { get; init; }
}

public record DisableInspectionRequest;

public record DisableInspectionResponse
{
    public bool Success { get; init; }
    public string? Message { get; init; }
}

// Moved to shmoxy.server.hooks namespace to avoid circular dependency
// This record is now defined in InspectionHook.cs
