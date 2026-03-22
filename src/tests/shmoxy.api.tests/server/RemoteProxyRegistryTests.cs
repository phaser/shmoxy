using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using shmoxy.api.data;
using shmoxy.api.ipc;
using shmoxy.api.models;
using shmoxy.api.server;

namespace shmoxy.api.tests.server;

public class RemoteProxyRegistryTests : IDisposable
{
    private readonly ProxiesDbContext _dbContext;
    private readonly Mock<ILogger<RemoteProxyRegistry>> _mockLogger;
    private readonly RemoteProxyRegistry _registry;

    public RemoteProxyRegistryTests()
    {
        var options = new DbContextOptionsBuilder<ProxiesDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ProxiesDbContext(options);
        _mockLogger = new Mock<ILogger<RemoteProxyRegistry>>();
        var loggerFactory = new LoggerFactory();
        _registry = new RemoteProxyRegistry(_dbContext, _mockLogger.Object, loggerFactory);
    }

    [Fact]
    public async Task RegisterAsync_CreatesProxy()
    {
        var proxy = new RemoteProxy
        {
            Name = "test-proxy",
            AdminUrl = "http://localhost:9090",
            ApiKey = "abc123"
        };

        var registered = await _registry.RegisterAsync(proxy);

        Assert.NotNull(registered);
        Assert.NotEmpty(registered.Id);
        Assert.Equal("test-proxy", registered.Name);
        Assert.Equal(RemoteProxyStatus.Unknown, registered.Status);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsProxy()
    {
        var proxy = new RemoteProxy
        {
            Name = "test-proxy",
            AdminUrl = "http://localhost:9090",
            ApiKey = "abc123"
        };

        var registered = await _registry.RegisterAsync(proxy);
        var retrieved = await _registry.GetByIdAsync(registered.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(registered.Id, retrieved.Id);
        Assert.Equal("test-proxy", retrieved.Name);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        var result = await _registry.GetByIdAsync("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllProxies()
    {
        await _registry.RegisterAsync(new RemoteProxy { Name = "proxy1", AdminUrl = "http://1:9090", ApiKey = "key1" });
        await _registry.RegisterAsync(new RemoteProxy { Name = "proxy2", AdminUrl = "http://2:9090", ApiKey = "key2" });

        var proxies = await _registry.GetAllAsync();

        Assert.Equal(2, proxies.Count);
    }

    [Fact]
    public async Task UnregisterAsync_RemovesProxy()
    {
        var proxy = new RemoteProxy
        {
            Name = "test-proxy",
            AdminUrl = "http://localhost:9090",
            ApiKey = "abc123"
        };

        var registered = await _registry.RegisterAsync(proxy);
        var deleted = await _registry.UnregisterAsync(registered.Id);

        Assert.True(deleted);

        var retrieved = await _registry.GetByIdAsync(registered.Id);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task UnregisterAsync_ReturnsFalse_WhenNotFound()
    {
        var result = await _registry.UnregisterAsync("nonexistent");

        Assert.False(result);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesProxy()
    {
        var proxy = new RemoteProxy
        {
            Name = "test-proxy",
            AdminUrl = "http://localhost:9090",
            ApiKey = "abc123"
        };

        var registered = await _registry.RegisterAsync(proxy);
        registered.ApiKey = "newkey";
        registered.AdminUrl = "http://newurl:9090";

        var updated = await _registry.UpdateAsync(registered);

        Assert.NotNull(updated);
        Assert.Equal("newkey", updated.ApiKey);
        Assert.Equal("http://newurl:9090", updated.AdminUrl);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsNull_WhenNotFound()
    {
        var proxy = new RemoteProxy
        {
            Id = "nonexistent",
            Name = "test",
            AdminUrl = "http://test:9090",
            ApiKey = "key"
        };

        var result = await _registry.UpdateAsync(proxy);

        Assert.Null(result);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
