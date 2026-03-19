namespace shmoxy.shared.ipc;

/// <summary>
/// Descriptor for a hook in the proxy's hook system.
/// </summary>
public record HookDescriptor
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty; // "builtin" | "script"
    public string? ScriptPath { get; init; }
    public bool Enabled { get; init; }
}
