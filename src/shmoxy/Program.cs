using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using shmoxy.ipc;
using shmoxy.models.configuration;
using shmoxy.server;
using shmoxy.server.hooks;

namespace shmoxy;

/// <summary>
/// CLI entry point for the proxy server.
/// </summary>
class Program
{
    static async Task<int> Main(string[] args)
    {
        // Define command-line options
        var portOption = new Option<int>(
            aliases: new[] { "--port", "-p" },
            description: "Listening port for the proxy server (default: 8080)",
            parseArgument: result =>
            {
                if (!int.TryParse(result.Tokens.FirstOrDefault()?.Value, out var port))
                    port = 8080;
                return port;
            });

        var certPathOption = new Option<string?>(
            aliases: new[] { "--cert" },
            description: "Path to TLS certificate file (for provided certs mode)");

        var keyPathOption = new Option<string?>(
            aliases: new[] { "--key" },
            description: "Path to TLS private key file (required with --cert)");

        var logLevelOption = new Option<ProxyConfig.LogLevelEnum>(
            aliases: new[] { "--log-level", "-l" },
            description: "Logging verbosity level (Debug, Info, Warn, Error)",
            parseArgument: result =>
            {
                if (!Enum.TryParse<ProxyConfig.LogLevelEnum>(result.Tokens.FirstOrDefault()?.Value, true, out var level))
                    level = ProxyConfig.LogLevelEnum.Info;
                return level;
            });

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
            var config = new ProxyConfig
            {
                Port = port,
                CertPath = certPath,
                KeyPath = keyPath,
                LogLevel = logLevel
            };

            // Validate cert/key pair if provided
            if ((certPath != null && keyPath == null) || (certPath == null && keyPath != null))
            {
                Console.Error.WriteLine("Error: Both --cert and --key must be specified together");
                Environment.Exit(1);
            }

            try
            {
                var inspectionHook = new InspectionHook();
                var hookChain = new InterceptHookChain().Add(inspectionHook);
                
                var server = new ProxyServer(config, hookChain);

                // Graceful shutdown handling
                using var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (_, __) => cts.Cancel();

                // Start IPC control API if socket path provided
                Task? ipcTask = null;
                if (!string.IsNullOrEmpty(ipcSocket))
                {
                    var stateService = new ProxyStateService(server, inspectionHook);
                    ipcTask = StartIpcApiAsync(ipcSocket, stateService, config, cts.Token);
                }

                var proxyTask = server.StartAsync(cts.Token);

                await Task.WhenAny(proxyTask, ipcTask ?? Task.CompletedTask);
                
                if (ipcTask != null)
                {
                    await ipcTask;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Fatal error: {ex.Message}");
                Environment.Exit(1);
            }
        }, portOption, certPathOption, keyPathOption, logLevelOption, ipcSocketOption);

        return await rootCommand.InvokeAsync(args);
    }

    private static async Task StartIpcApiAsync(string socketPath, ProxyStateService stateService, ProxyConfig config, CancellationToken cancellationToken)
    {
        var host = new WebHostBuilder()
            .UseKestrel(kestrelOptions =>
            {
                kestrelOptions.ListenUnixSocket(socketPath);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapProxyControlApi(stateService, config);
                });
            })
            .Build();

        await host.RunAsync(cancellationToken);
    }
}
