using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.Playwright;
using Xunit;

namespace shmoxy.frontend.tests;

[CollectionDefinition("Frontend")]
public class FrontendCollection : ICollectionFixture<FrontendTestFixture>;

public class FrontendTestFixture : IAsyncLifetime
{
    private WebApplication? _app;
    private IPlaywright? _playwright;

    public IBrowser? Browser { get; private set; }
    public string BaseUrl { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var port = GetAvailablePort();
        BaseUrl = $"http://localhost:{port}";

        var apiProjectDir = FindProjectDir("shmoxy.api");

        // Set environment variables so the WebApplicationBuilder resolves
        // static web assets from the correct manifest (shmoxy.api.staticwebassets.runtime.json)
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("ASPNETCORE_APPLICATIONNAME", "shmoxy.api");

        // Use a dynamic port for the proxy to avoid conflicts with port 8080
        var proxyPort = GetAvailablePort();
        _app = Program.CreateApp(["--urls", BaseUrl, "--contentRoot", apiProjectDir,
            "--ApiConfig:ProxyPort", proxyPort.ToString()]);

        // Start the app in the background
        _ = _app.RunAsync();

        // Wait for the server to be ready
        using var client = new HttpClient();
        for (var i = 0; i < 50; i++)
        {
            try
            {
                var response = await client.GetAsync($"{BaseUrl}/api/health");
                if (response.IsSuccessStatusCode) break;
            }
            catch
            {
                // Server not ready yet
            }
            await Task.Delay(100);
        }

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
    }

    public async Task<IPage> CreatePageAsync()
    {
        var context = await Browser!.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true
        });
        return await context.NewPageAsync();
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null)
            await Browser.DisposeAsync();

        _playwright?.Dispose();

        if (_app is not null)
            await _app.StopAsync();
    }

    private static string FindProjectDir(string projectName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, projectName);
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new InvalidOperationException($"Could not find {projectName} directory");
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
