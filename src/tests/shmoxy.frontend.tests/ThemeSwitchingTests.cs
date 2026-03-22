using Microsoft.Playwright;
using Xunit;

namespace shmoxy.frontend.tests;

[Collection("Frontend")]
public class ThemeSwitchingTests
{
    private readonly FrontendTestFixture _fixture;

    public ThemeSwitchingTests(FrontendTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PageLoads_WithTitle()
    {
        var page = await _fixture.CreatePageAsync();
        await page.GotoAsync($"{_fixture.BaseUrl}/", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 30000
        });

        var title = await page.TitleAsync();
        Assert.Equal("Shmoxy", title);
    }

    [Fact]
    public async Task ThemeToggle_ChangesTheme()
    {
        var page = await _fixture.CreatePageAsync();
        await page.GotoAsync($"{_fixture.BaseUrl}/", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 30000
        });

        // Wait for Blazor SignalR circuit to connect and enable interactivity
        await page.WaitForTimeoutAsync(5000);

        // Find theme toggle button by title attribute
        var toggleButton = page.GetByTitle("Switch to light mode");
        var count = await toggleButton.CountAsync();
        if (count == 0)
        {
            toggleButton = page.GetByTitle("Switch to dark mode");
        }
        await toggleButton.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });

        // Click and retry — the circuit may need a moment after becoming connected
        string? storedTheme = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await toggleButton.ClickAsync();
            await page.WaitForTimeoutAsync(1000);

            storedTheme = await page.EvaluateAsync<string?>(
                "() => { const v = localStorage.getItem('preferred-theme'); return v ? JSON.parse(v) : null; }");
            if (storedTheme is not null)
                break;
        }

        Assert.NotNull(storedTheme);
        Assert.True(storedTheme == "light" || storedTheme == "dark",
            $"Expected 'light' or 'dark', got '{storedTheme}'");
    }

    [Fact]
    public async Task NavigationMenu_HasExpectedLinks()
    {
        var page = await _fixture.CreatePageAsync();
        await page.GotoAsync($"{_fixture.BaseUrl}/", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 30000
        });

        await page.WaitForTimeoutAsync(2000);

        // FluentNavLink renders as <a> inside <fluent-nav-link> or as nav links
        var navLinks = page.Locator("nav a[href]");
        var count = await navLinks.CountAsync();
        Assert.True(count >= 3, $"Expected at least 3 nav links, found {count}");
    }

    [Fact]
    public async Task DashboardPage_LoadsCards()
    {
        var page = await _fixture.CreatePageAsync();
        await page.GotoAsync($"{_fixture.BaseUrl}/", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 30000
        });

        await page.WaitForTimeoutAsync(2000);

        var cards = page.Locator("fluent-card");
        var count = await cards.CountAsync();
        Assert.Equal(3, count);
    }
}
