using shmoxy.api.models;

namespace shmoxy.api.server;

public interface IProxyProcessManager
{
    Task<ProxyInstanceState> StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
    Task<ProxyInstanceState?> GetStateAsync();
    Task<bool> IsRunningAsync();
    Task<string> GetRootCertPemAsync(CancellationToken ct = default);
    Task<byte[]> GetRootCertDerAsync(CancellationToken ct = default);
    event EventHandler<ProxyInstanceState>? OnStateChanged;
}
