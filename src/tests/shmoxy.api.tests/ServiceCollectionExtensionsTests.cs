using Microsoft.Extensions.DependencyInjection;
using shmoxy.api.ipc;

namespace shmoxy.api.tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddUnixSocketIpcClient_RegistersClient()
    {
        var services = new ServiceCollection();
        var socketPath = "/tmp/test.sock";

        services.AddUnixSocketIpcClient(socketPath);

        var provider = services.BuildServiceProvider();
        var client = provider.GetService<IProxyIpcClient>();

        Assert.NotNull(client);
        Assert.IsType<ProxyIpcClient>(client);
    }

    [Fact]
    public void AddHttpIpcClient_RegistersClient()
    {
        var services = new ServiceCollection();
        var baseUrl = "http://localhost:9090";
        var apiKey = "test-api-key";

        services.AddHttpIpcClient(baseUrl, apiKey);

        var provider = services.BuildServiceProvider();
        var client = provider.GetService<IProxyIpcClient>();

        Assert.NotNull(client);
        Assert.IsType<ProxyIpcClient>(client);
    }

    [Fact]
    public void AddProxyIpcClient_CanResolveClient()
    {
        var services = new ServiceCollection();

        services.AddProxyIpcClient();

        var provider = services.BuildServiceProvider();
        var client = provider.GetService<IProxyIpcClient>();

        Assert.NotNull(client);
        Assert.IsType<ProxyIpcClient>(client);
    }
}
