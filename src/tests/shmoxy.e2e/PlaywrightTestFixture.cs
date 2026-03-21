using Microsoft.Playwright;
using Xunit;

namespace shmoxy.e2e;

public sealed class PlaywrightFixture : IAsyncLifetime
{
    public IBrowser Browser { get; private set; } = null!;
    public string BaseUrl { get; private set; } = "";
    private IPlaywright? _playwright;

    public async Task InitializeAsync()
    {
        BaseUrl = Environment.GetEnvironmentVariable("SHMOXY_BASE_URL") ?? "http://localhost:5000";

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new()
        {
            Headless = true,
            Args = new[] { "--no-sandbox", "--disable-setuid-sandbox" }
        });
    }

    public async Task DisposeAsync()
    {
        await Browser.CloseAsync();
        _playwright?.Dispose();
    }

    public async Task<IBrowserContext> CreateContextAsync()
    {
        return await Browser.NewContextAsync(new()
        {
            IgnoreHTTPSErrors = true,
            Proxy = new() { Server = "none" }
        });
    }
}
