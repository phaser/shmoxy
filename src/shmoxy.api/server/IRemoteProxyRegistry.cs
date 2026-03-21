using shmoxy.api.models;

namespace shmoxy.api.server;

public interface IRemoteProxyRegistry
{
    Task<RemoteProxy> RegisterAsync(RemoteProxy proxy, CancellationToken ct = default);
    Task<RemoteProxy?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<RemoteProxy>> GetAllAsync(CancellationToken ct = default);
    Task<bool> UnregisterAsync(string id, CancellationToken ct = default);
    Task<RemoteProxy?> UpdateAsync(RemoteProxy proxy, CancellationToken ct = default);
    Task<bool> TestConnectivityAsync(string url, string apiKey, CancellationToken ct = default);
}
