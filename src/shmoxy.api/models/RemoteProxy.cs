namespace shmoxy.api.models;

public enum RemoteProxyStatus
{
    Unknown,
    Healthy,
    Unhealthy,
    Unreachable
}

public class RemoteProxy
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string AdminUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public RemoteProxyStatus Status { get; set; } = RemoteProxyStatus.Unknown;
    public DateTime? LastHealthCheck { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
