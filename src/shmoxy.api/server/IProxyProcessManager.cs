using shmoxy.api.ipc;
using shmoxy.api.models;

namespace shmoxy.api.server;

public interface IProxyProcessManager
{
    Task<ProxyInstanceState> StartAsync(CancellationToken ct = default);
    Task StopAsync(ShutdownSource source = ShutdownSource.User, CancellationToken ct = default);
    Task<ProxyInstanceState> RestartAsync(int? portOverride = null, CancellationToken ct = default);
    Task<ProxyInstanceState?> GetStateAsync();
    Task<bool> IsRunningAsync();
    Task<string> GetRootCertPemAsync(CancellationToken ct = default);
    Task<byte[]> GetRootCertDerAsync(CancellationToken ct = default);
    IProxyIpcClient GetIpcClient();
    event EventHandler<ProxyInstanceState>? OnStateChanged;
}
