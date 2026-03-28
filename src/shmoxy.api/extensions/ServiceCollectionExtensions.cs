using System.Net;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using shmoxy.api.data;
using shmoxy.api.ipc;
using shmoxy.api.models.configuration;
using shmoxy.api.server;

namespace shmoxy.api;

/// <summary>
/// Extension methods for registering ProxyIpcClient in DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddProxyIpcClient(
        this IServiceCollection services,
        string name = "default")
    {
        services.AddHttpClient<IProxyIpcClient, ProxyIpcClient>(name);
        return services;
    }

    public static IServiceCollection AddUnixSocketIpcClient(
        this IServiceCollection services,
        string socketPath)
    {
        services.AddHttpClient<IProxyIpcClient, ProxyIpcClient>("unixsocket", (sp, client) =>
        {
            client.BaseAddress = new Uri("http://localhost");
        })
        .ConfigurePrimaryHttpMessageHandler(sp =>
        {
            var handler = new SocketsHttpHandler
            {
                ConnectCallback = async (context, token) =>
                {
                    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    var endPoint = new UnixDomainSocketEndPoint(socketPath);
                    await socket.ConnectAsync(endPoint, token);
                    return new NetworkStream(socket, ownsSocket: true);
                }
            };
            return handler;
        });

        return services;
    }

    public static IServiceCollection AddHttpIpcClient(
        this IServiceCollection services,
        string baseUrl,
        string apiKey)
    {
        services.AddHttpClient<IProxyIpcClient, ProxyIpcClient>("http", (sp, client) =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        });

        return services;
    }

    public static IServiceCollection AddRemoteProxyRegistry(this IServiceCollection services)
    {
        services.AddScoped<IRemoteProxyRegistry, RemoteProxyRegistry>();
        services.AddHostedService<RemoteProxyHealthMonitor>();
        return services;
    }

    public static IServiceCollection AddSessionRepository(this IServiceCollection services)
    {
        services.AddScoped<ISessionRepository, SessionRepository>();
        return services;
    }

    public static IServiceCollection AddSqliteDbContext(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<ProxiesDbContext>(options =>
            options.UseSqlite(connectionString));
        return services;
    }
}
