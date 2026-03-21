namespace shmoxy.api.models.dto;

public class RegisterRemoteProxyRequest
{
    public string Name { get; set; } = string.Empty;
    public string AdminUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
}

public class RemoteProxyResponse
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AdminUrl { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? LastHealthCheck { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class UpdateRemoteProxyRequest
{
    public string? ApiKey { get; set; }
}
