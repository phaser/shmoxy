using Microsoft.Playwright;
using NUnit.Framework;

namespace shmoxy.e2e;

[SetUpFixture]
public sealed class PlaywrightSetup : IDisposable
{
    public static IPlaywright? SharedPlaywright { get; private set; }

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _ = await Microsoft.Playwright.Program.MainAsync(new[] { "install" });
        
        var playwrightTask = Microsoft.Playwright.CreateAsync();
        SharedPlaywright = await playwrightTask;
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        if (SharedPlaywright != null)
            SharedPlaywright.Dispose();
    }

    public void Dispose() => OneTimeTearDown();
}

[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class PlaywrightTestFixture : IDisposable, IAsyncDisposable
{
    public IBrowser Browser { get; private set; } = null!;
    public string BaseUrl { get; private set; } = "";

    [SetUp]
    public async Task SetUp()
    {
        if (PlaywrightSetup.SharedPlaywright == null)
            throw new InvalidOperationException("Playwright not initialized");

        BaseUrl = Environment.GetEnvironmentVariable("SHMOXY_BASE_URL") ?? "http://localhost:5000";
        
        Browser = await PlaywrightSetup.SharedPlaywright.Chromium.LaunchAsync(new BrowserLaunchOptions
        {
            Headless = true,
            Args = new[] { "--no-sandbox", "--disable-setuid-sandbox" }
        });
    }

    public async Task<IBrowserContext> CreateContextAsync()
    {
        return await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true
        });
    }

    [TearDown]
    public void TearDown() => DisposeAsync().AsTask().Wait();

    public async ValueTask DisposeAsync()
    {
        await Browser.CloseAsync();
    }

    public void Dispose() => DisposeAsync().AsTask().Wait();
}
