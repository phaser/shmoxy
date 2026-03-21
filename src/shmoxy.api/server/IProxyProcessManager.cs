using shmoxy.api.models;

namespace shmoxy.api.server;

public interface IProxyProcessManager
{
    Task<ProxyInstanceState> StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
    Task<ProxyInstanceState?> GetStateAsync();
    Task<bool> IsRunningAsync();
    event EventHandler<ProxyInstanceState>? OnStateChanged;
}
