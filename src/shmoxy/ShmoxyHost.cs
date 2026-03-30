using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using shmoxy.ipc;
using shmoxy.server;
using shmoxy.server.hooks;
using shmoxy.shared.ipc;

namespace shmoxy;

/// <summary>
/// Configures the host for the shmoxy proxy server.
/// Shared between Program.cs and integration tests.
/// </summary>
public static class ShmoxyHost
{
    /// <summary>
    /// Configures the host builder with shmoxy-specific settings.
    /// </summary>
    public static IHostBuilder CreateHostBuilder(string[] args)
    {
        var builder = Host.CreateDefaultBuilder(args);

        builder.ConfigureAppConfiguration(ConfigureAppConfiguration);
        builder.ConfigureServices(ConfigureServices);

        return builder;
    }

    /// <summary>
    /// Configures app configuration for shmoxy.
    /// </summary>
    public static void ConfigureAppConfiguration(HostBuilderContext context, IConfigurationBuilder config)
    {
        config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "ProxyConfig:Port", "8080" },
            { "ProxyConfig:LogLevel", "0" },
        }!);
    }

    /// <summary>
    /// Configures logging for shmoxy.
    /// </summary>
    public static void ConfigureLogging(ILoggingBuilder logging)
    {
        logging.AddConsole();
        logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);
    }

    /// <summary>
    /// Configures dependency injection for shmoxy.
    /// </summary>
    public static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        services.Configure<ProxyConfig>(context.Configuration.GetSection("ProxyConfig"));
        services.Configure<IpcOptions>(context.Configuration.GetSection("IpcOptions"));

        services.AddSingleton<SessionLogBuffer>(sp =>
        {
            var config = sp.GetRequiredService<IOptions<ProxyConfig>>().Value;
            return new SessionLogBuffer { Enabled = config.SessionLoggingEnabled };
        });
        services.AddSingleton<ILoggerProvider>(sp =>
            new SessionLogBufferProvider(sp.GetRequiredService<SessionLogBuffer>()));
        services.AddSingleton<InspectionHook>();
        services.AddSingleton<InterceptHookChain>(sp =>
        {
            var inspectionHook = sp.GetRequiredService<InspectionHook>();
            return new InterceptHookChain()
                .Add(inspectionHook);
        });

        services.AddSingleton<ProxyServer>(sp =>
        {
            var config = sp.GetRequiredService<IOptions<ProxyConfig>>().Value;
            var hookChain = sp.GetRequiredService<InterceptHookChain>();
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<ProxyServer>();
            return new ProxyServer(config, hookChain, logger);
        });

        services.AddSingleton<ProxyStateService>(sp =>
        {
            var proxy = sp.GetRequiredService<ProxyServer>();
            var inspectionHook = sp.GetRequiredService<InspectionHook>();
            var logBuffer = sp.GetRequiredService<SessionLogBuffer>();
            return new ProxyStateService(proxy, inspectionHook, logBuffer);
        });

        services.AddHostedService<ProxyHostedService>();

        var ipcSocket = context.Configuration["IpcOptions:SocketPath"];
        var adminPort = context.Configuration["IpcOptions:AdminPort"];

        if (!string.IsNullOrEmpty(ipcSocket) || (!string.IsNullOrEmpty(adminPort) && int.Parse(adminPort) > 0))
        {
            services.AddHostedService<IpcHostedService>();
        }
    }

    /// <summary>
    /// Creates a minimal IHost for testing IPC endpoints in isolation.
    /// </summary>
    public static IHost CreateIpcHost(ProxyStateService stateService, ProxyConfig config, string socketPath)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseKestrel(kestrelOptions =>
                {
                    kestrelOptions.ListenUnixSocket(socketPath);
                })
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton(stateService);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        var loggerFactory = app.ApplicationServices.GetRequiredService<ILoggerFactory>();
                        endpoints.MapProxyControlApi(stateService, config, loggerFactory);
                    });
                });
            })
            .Build();
    }
}
