using System.CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using shmoxy.ipc;
using shmoxy.models.configuration;
using shmoxy.server;
using shmoxy.server.hooks;

namespace shmoxy;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var portOption = new Option<int>(
            aliases: new[] { "--port", "-p" },
            description: "Listening port for the proxy server (default: 8080)",
            getDefaultValue: () => 8080);

        var certPathOption = new Option<string?>(
            aliases: new[] { "--cert" },
            description: "Path to TLS certificate file (for provided certs mode)");

        var keyPathOption = new Option<string?>(
            aliases: new[] { "--key" },
            description: "Path to TLS private key file (required with --cert)");

        var logLevelOption = new Option<ProxyConfig.LogLevelEnum>(
            aliases: new[] { "--log-level", "-l" },
            description: "Logging verbosity level (Debug, Info, Warn, Error)",
            getDefaultValue: () => ProxyConfig.LogLevelEnum.Info);

        var ipcSocketOption = new Option<string?>(
            aliases: new[] { "--ipc-socket" },
            description: "Unix Domain Socket path for IPC control API (optional)");

        RootCommand rootCommand = new RootCommand("Shmoxy HTTP/HTTPS Intercepting Proxy");
        rootCommand.AddOption(portOption);
        rootCommand.AddOption(certPathOption);
        rootCommand.AddOption(keyPathOption);
        rootCommand.AddOption(logLevelOption);
        rootCommand.AddOption(ipcSocketOption);

        rootCommand.SetHandler(async (port, certPath, keyPath, logLevel, ipcSocket) =>
        {
            if ((certPath != null && keyPath == null) || (certPath == null && keyPath != null))
            {
                Console.Error.WriteLine("Error: Both --cert and --key must be specified together");
                Environment.Exit(1);
            }

            var builder = Host.CreateDefaultBuilder(args);
            
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "ProxyConfig:Port", port.ToString() },
                    { "ProxyConfig:CertPath", certPath },
                    { "ProxyConfig:KeyPath", keyPath },
                    { "ProxyConfig:LogLevel", logLevel.ToString() },
                    { "IpcOptions:SocketPath", ipcSocket },
                }!);
            });

            builder.ConfigureLogging((context, logging) =>
            {
                logging.AddConsole();
                logging.SetMinimumLevel(logLevel switch
                {
                    ProxyConfig.LogLevelEnum.Debug => LogLevel.Debug,
                    ProxyConfig.LogLevelEnum.Info => LogLevel.Information,
                    ProxyConfig.LogLevelEnum.Warn => LogLevel.Warning,
                    ProxyConfig.LogLevelEnum.Error => LogLevel.Error,
                    _ => LogLevel.Information
                });
            });

            builder.ConfigureServices((context, services) =>
            {
                services.Configure<ProxyConfig>(context.Configuration.GetSection("ProxyConfig"));
                services.Configure<IpcOptions>(context.Configuration.GetSection("IpcOptions"));

                services.AddSingleton<InspectionHook>();
                services.AddSingleton<InterceptHookChain>(sp =>
                {
                    var inspectionHook = sp.GetRequiredService<InspectionHook>();
                    return new InterceptHookChain().Add(inspectionHook);
                });

                services.AddSingleton<ProxyServer>(sp =>
                {
                    var config = sp.GetRequiredService<IOptions<ProxyConfig>>().Value;
                    var hookChain = sp.GetRequiredService<InterceptHookChain>();
                    return new ProxyServer(config, hookChain);
                });

                services.AddSingleton<ProxyStateService>(sp =>
                {
                    var proxy = sp.GetRequiredService<ProxyServer>();
                    var inspectionHook = sp.GetRequiredService<InspectionHook>();
                    return new ProxyStateService(proxy, inspectionHook);
                });

                services.AddHostedService<ProxyHostedService>();

                if (!string.IsNullOrEmpty(ipcSocket))
                {
                    services.AddHostedService<IpcHostedService>();
                }
            });

            var host = builder.Build();
            await host.RunAsync();
        }, portOption, certPathOption, keyPathOption, logLevelOption, ipcSocketOption);

        return await rootCommand.InvokeAsync(args);
    }
}
