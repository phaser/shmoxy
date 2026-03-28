namespace shmoxy.shared.ipc;

/// <summary>
/// Request to dismiss a passthrough suggestion for a host.
/// </summary>
public class DismissSuggestionRequest
{
    public string Host { get; set; } = string.Empty;
}

/// <summary>
/// Request to accept a passthrough suggestion, adding the host to the passthrough list.
/// </summary>
public class AcceptSuggestionRequest
{
    public string Host { get; set; } = string.Empty;
}
