namespace shmoxy.api.models.configuration;

/// <summary>
/// Configurable timeout levels for IPC operations.
/// </summary>
public static class IpcTimeouts
{
    public static readonly TimeSpan Small = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan Medium = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan Long = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan VeryLong = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan Streaming = TimeSpan.FromSeconds(60);
}
