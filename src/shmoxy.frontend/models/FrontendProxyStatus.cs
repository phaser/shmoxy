namespace shmoxy.frontend.services;

public record FrontendProxyStatus(bool IsRunning, string? Address = null, long RequestCount = 0, string? ErrorMessage = null)
{
    public static readonly FrontendProxyStatus Stopped = new(IsRunning: false);
}
