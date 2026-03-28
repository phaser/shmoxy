namespace shmoxy.shared.ipc;

/// <summary>
/// Describes a registered passthrough detector and its enabled state.
/// </summary>
public class DetectorDescriptor
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}
