using Microsoft.Playwright;
using shmoxy.server;
using shmoxy.shared.ipc;
using Xunit;

namespace shmoxy.e2e;

public sealed class ProxyTestFixture : IAsyncLifetime
{
    public IBrowser Browser { get; private set; } = null!;
    public int Port { get; private set; }
    public string BaseUrl => $"http://127.0.0.1:{Port}";
    public string ArtifactsDir { get; private set; } = "";
    private IPlaywright? _playwright;
    private ProxyServer? _server;
    private CancellationTokenSource? _cts;

    public async Task InitializeAsync()
    {
        var config = new ProxyConfig { Port = 0, LogLevel = ProxyConfig.LogLevelEnum.Debug };

        _server = new ProxyServer(config);
        _cts = new CancellationTokenSource();

        // Start server in background
        _ = _server.StartAsync(_cts.Token);

        for (int i = 0; i < 20 && !_server.IsListening; i++)
            await Task.Delay(50);

        if (!_server.IsListening)
            throw new InvalidOperationException("Proxy server failed to start");

        Port = _server.ListeningPort;
        Console.WriteLine($"Proxy started on port {Port}");

        // Create artifacts directory with random identifier
        var randomId = Guid.NewGuid().ToString("N")[..8];
        ArtifactsDir = Path.Combine(Directory.GetCurrentDirectory(), "playwright_run_" + randomId);
        Directory.CreateDirectory(ArtifactsDir);
        Console.WriteLine($"Artifacts will be saved to: {ArtifactsDir}");

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new()
        {
            Headless = true,
            Args = new[] { "--no-sandbox", "--disable-setuid-sandbox", "--disable-gpu", "--ignore-certificate-errors" },
            Timeout = 30000
        });
        Console.WriteLine("Browser launched");
    }

    public async Task DisposeAsync()
    {
        await Browser.CloseAsync();
        _playwright?.Dispose();

        if (_cts != null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
        }

        if (_server != null)
            await _server.DisposeAsync();
    }

    /// <summary>
    /// Creates a browser context with tracing enabled.
    /// Call this in your test and pass the test name for proper artifact organization.
    /// </summary>
    public async Task<IBrowserContext> CreateContextWithTracingAsync(string testName, bool useProxy = false)
    {
        var sanitizedTestName = SanitizeFileName(testName);
        var testArtifactsDir = Path.Combine(ArtifactsDir, sanitizedTestName);
        Directory.CreateDirectory(testArtifactsDir);

        var contextOptions = new BrowserNewContextOptions
        {
            // When routing through the proxy, the browser must trust the proxy's
            // dynamically generated MITM certificates for HTTPS sites.
            IgnoreHTTPSErrors = useProxy
        };

        // Only set proxy if explicitly requested
        if (useProxy)
        {
            contextOptions.Proxy = new() { Server = $"http://127.0.0.1:{Port}" };
        }

        var context = await Browser.NewContextAsync(contextOptions);

        // Start tracing
        await context.Tracing.StartAsync(new()
        {
            Screenshots = true,
            Snapshots = true,
            Sources = true
        });

        return context;
    }

    /// <summary>
    /// Stops tracing and saves artifacts (trace, screenshots) to the test's artifacts directory.
    /// Call this at the end of each test.
    /// </summary>
    public async Task SaveTracingAsync(IBrowserContext context, string testName, bool success = true)
    {
        var sanitizedTestName = SanitizeFileName(testName);
        var testArtifactsDir = Path.Combine(ArtifactsDir, sanitizedTestName);
        Directory.CreateDirectory(testArtifactsDir);

        var tracePath = Path.Combine(testArtifactsDir, $"trace{(success ? "" : "_failed")}.zip");
        await context.Tracing.StopAsync(new() { Path = tracePath });

        // Take screenshot
        var screenshotPath = Path.Combine(testArtifactsDir, $"screenshot{(success ? "" : "_failed")}.png");
        try
        {
            var page = context.Pages.FirstOrDefault();
            if (page != null)
            {
                await page.ScreenshotAsync(new() { Path = screenshotPath, FullPage = true });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not take screenshot: {ex.Message}");
        }

        Console.WriteLine($"Artifacts saved to: {testArtifactsDir}");
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name.Length > 100 ? name[..100] : name;
    }
}
