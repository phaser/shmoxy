using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using shmoxy.api.data;
using shmoxy.api.ipc;
using shmoxy.api.models;

namespace shmoxy.api.server;

public class RemoteProxyRegistry : IRemoteProxyRegistry
{
    private readonly ProxiesDbContext _dbContext;
    private readonly ILogger<RemoteProxyRegistry> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public RemoteProxyRegistry(
        ProxiesDbContext dbContext,
        ILogger<RemoteProxyRegistry> logger,
        ILoggerFactory loggerFactory)
    {
        _dbContext = dbContext;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    public async Task<RemoteProxy> RegisterAsync(RemoteProxy proxy, CancellationToken ct = default)
    {
        proxy.Id = Guid.NewGuid().ToString();
        proxy.CreatedAt = DateTime.UtcNow;
        proxy.UpdatedAt = DateTime.UtcNow;
        proxy.Status = RemoteProxyStatus.Unknown;

        _dbContext.RemoteProxies.Add(proxy);
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Registered remote proxy {Name} ({Id})", proxy.Name, proxy.Id);
        return proxy;
    }

    public async Task<RemoteProxy?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        return await _dbContext.RemoteProxies.FindAsync(new object[] { id }, ct);
    }

    public async Task<IReadOnlyList<RemoteProxy>> GetAllAsync(CancellationToken ct = default)
    {
        return await _dbContext.RemoteProxies.ToListAsync(ct);
    }

    public async Task<bool> UnregisterAsync(string id, CancellationToken ct = default)
    {
        var proxy = await _dbContext.RemoteProxies.FindAsync(new object[] { id }, ct);
        if (proxy == null)
        {
            return false;
        }

        _dbContext.RemoteProxies.Remove(proxy);
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Unregistered remote proxy {Name} ({Id})", proxy.Name, proxy.Id);
        return true;
    }

    public async Task<RemoteProxy?> UpdateAsync(RemoteProxy proxy, CancellationToken ct = default)
    {
        var existing = await _dbContext.RemoteProxies.FindAsync(new object[] { proxy.Id }, ct);
        if (existing == null)
        {
            return null;
        }

        existing.AdminUrl = proxy.AdminUrl;
        existing.ApiKey = proxy.ApiKey;
        existing.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Updated remote proxy {Name} ({Id})", existing.Name, existing.Id);
        return existing;
    }

    public async Task<bool> TestConnectivityAsync(string url, string apiKey, CancellationToken ct = default)
    {
        try
        {
            using var handler = new HttpClientHandler();
            using var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(url),
                Timeout = TimeSpan.FromSeconds(5)
            };
            httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);

            var tempLogger = _loggerFactory.CreateLogger<ProxyIpcClient>();
            using var tempClient = new ProxyIpcClient(httpClient, tempLogger);
            return await tempClient.IsHealthyAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Connectivity test failed for {Url}", url);
            return false;
        }
    }
}
