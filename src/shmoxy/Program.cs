using System;
using System.CommandLine;
using System.CommandLine.Invocation;

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

            RootCommand rootCommand = new RootCommand("Shmoxy HTTP/HTTPS Proxy Server with TLS Termination");
        rootCommand.AddOption(portOption);
        rootCommand.AddOption(certPathOption);
        rootCommand.AddOption(keyPathOption);
        rootCommand.AddOption(logLevelOption);

        rootCommand.SetHandler(async (port, certPath, keyPath, logLevel) =>
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
                var server = new ProxyServer(config);

                // Graceful shutdown handling
                using var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (_, __) => cts.Cancel();

                await server.StartAsync(cts.Token);
                await Task.Delay(-1, cts.Token); // Wait for cancellation
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Fatal error: {ex.Message}");
                Environment.Exit(1);
            }
        }, portOption, certPathOption, keyPathOption, logLevelOption);

        return await rootCommand.InvokeAsync(args);
    }
}
