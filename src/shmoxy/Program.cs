using System.CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using shmoxy.shared.ipc;

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

        var adminPortOption = new Option<int?>(
            aliases: new[] { "--admin-port" },
            description: "TCP port for HTTP admin API (optional, requires X-API-Key authentication)");

        var inspectionCaptureLimitOption = new Option<int>(
            aliases: new[] { "--inspection-capture-limit" },
            description: "Maximum body preview bytes retained for interception (default: 1048576; 0 disables body capture)",
            getDefaultValue: () => 1_048_576);

        RootCommand rootCommand = new RootCommand("Shmoxy HTTP/HTTPS Intercepting Proxy");
        rootCommand.AddOption(portOption);
        rootCommand.AddOption(certPathOption);
        rootCommand.AddOption(keyPathOption);
        rootCommand.AddOption(logLevelOption);
        rootCommand.AddOption(ipcSocketOption);
        rootCommand.AddOption(adminPortOption);
        rootCommand.AddOption(inspectionCaptureLimitOption);

        rootCommand.SetHandler(async (
            port,
            certPath,
            keyPath,
            logLevel,
            ipcSocket,
            adminPort,
            inspectionCaptureLimit) =>
        {
            if ((certPath != null && keyPath == null) || (certPath == null && keyPath != null))
            {
                Console.Error.WriteLine("Error: Both --cert and --key must be specified together");
                Environment.Exit(1);
            }

            if (inspectionCaptureLimit < 0)
            {
                Console.Error.WriteLine("Error: --inspection-capture-limit cannot be negative");
                Environment.Exit(1);
            }

            var builder = ShmoxyHost.CreateHostBuilder(args);

            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "ProxyConfig:Port", port.ToString() },
                    { "ProxyConfig:CertPath", certPath },
                    { "ProxyConfig:KeyPath", keyPath },
                    { "ProxyConfig:LogLevel", logLevel.ToString() },
                    { "ProxyConfig:InspectionCaptureLimitBytes", inspectionCaptureLimit.ToString() },
                    { "IpcOptions:SocketPath", ipcSocket },
                    { "IpcOptions:AdminPort", adminPort?.ToString() },
                }!);
            });

            builder.ConfigureLogging(logging =>
            {
                logging.AddConsole();
                logging.SetMinimumLevel(logLevel switch
                {
                    ProxyConfig.LogLevelEnum.Debug => Microsoft.Extensions.Logging.LogLevel.Debug,
                    ProxyConfig.LogLevelEnum.Info => Microsoft.Extensions.Logging.LogLevel.Information,
                    ProxyConfig.LogLevelEnum.Warn => Microsoft.Extensions.Logging.LogLevel.Warning,
                    ProxyConfig.LogLevelEnum.Error => Microsoft.Extensions.Logging.LogLevel.Error,
                    _ => Microsoft.Extensions.Logging.LogLevel.Information
                });
            });

            var host = builder.Build();
            await host.RunAsync();
        },
        portOption,
        certPathOption,
        keyPathOption,
        logLevelOption,
        ipcSocketOption,
        adminPortOption,
        inspectionCaptureLimitOption);

        return await rootCommand.InvokeAsync(args);
    }
}
