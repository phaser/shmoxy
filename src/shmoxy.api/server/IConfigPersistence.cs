using shmoxy.shared.ipc;

namespace shmoxy.api.server;

public interface IConfigPersistence
{
    Task<ProxyConfig?> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(ProxyConfig config, CancellationToken ct = default);
}
