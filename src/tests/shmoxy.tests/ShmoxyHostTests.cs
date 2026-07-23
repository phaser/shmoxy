using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using shmoxy.server;
using shmoxy.server.hooks;

namespace shmoxy.tests;

public class ShmoxyHostTests
{
    [Fact]
    public void CreateHostBuilder_WithoutControlApi_UsesStandaloneProxyEngine()
    {
        var certDirectory = Path.Combine(
            Path.GetTempPath(),
            $"shmoxy-standalone-{Guid.NewGuid():N}");

        try
        {
            using var host = ShmoxyHost.CreateHostBuilder(Array.Empty<string>())
                .ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ProxyConfig:Port"] = "0",
                        ["ProxyConfig:CertStoragePath"] = certDirectory,
                        ["IpcOptions:SocketPath"] = null,
                        ["IpcOptions:AdminPort"] = null
                    });
                })
                .Build();

            Assert.NotNull(host.Services.GetRequiredService<ProxyServer>());
            Assert.Null(host.Services.GetService<InspectionHook>());
            Assert.Null(host.Services.GetService<BreakpointHook>());
            Assert.Null(host.Services.GetService<InterceptHookChain>());
        }
        finally
        {
            if (Directory.Exists(certDirectory))
                Directory.Delete(certDirectory, recursive: true);
        }
    }

    [Fact]
    public void CreateHostBuilder_WithControlApi_AddsOptionalInspectionAdapters()
    {
        var certDirectory = Path.Combine(
            Path.GetTempPath(),
            $"shmoxy-controlled-{Guid.NewGuid():N}");
        var socketPath = Path.Combine(
            Path.GetTempPath(),
            $"shmoxy-controlled-{Guid.NewGuid():N}.sock");

        try
        {
            using var host = ShmoxyHost.CreateHostBuilder(Array.Empty<string>())
                .ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ProxyConfig:Port"] = "0",
                        ["ProxyConfig:CertStoragePath"] = certDirectory,
                        ["IpcOptions:SocketPath"] = socketPath
                    });
                })
                .Build();

            Assert.NotNull(host.Services.GetRequiredService<ProxyServer>());
            Assert.NotNull(host.Services.GetService<InspectionHook>());
            Assert.NotNull(host.Services.GetService<BreakpointHook>());
            Assert.NotNull(host.Services.GetService<InterceptHookChain>());
        }
        finally
        {
            if (Directory.Exists(certDirectory))
                Directory.Delete(certDirectory, recursive: true);
        }
    }
}
