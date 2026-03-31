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
        await page.GotoAsync($"{_fixture.BaseUrl}/settings", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 30000
        });

        // Wait for Blazor SignalR circuit to connect and enable interactivity
        await page.WaitForTimeoutAsync(5000);

        // Expand the Appearance accordion item to reveal the theme switch
        var appearanceItem = page.Locator("fluent-accordion-item").First;
        await appearanceItem.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        // Capture initial luminance to verify it changes after toggle
        var initialLuminance = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.documentElement).getPropertyValue('--base-layer-luminance').trim()");

        // Find the theme switch (first one on the page) and click it to toggle
        var themeSwitch = page.Locator("fluent-switch").First;
        await themeSwitch.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });

        await themeSwitch.ClickAsync();
        await page.WaitForTimeoutAsync(1500);

        // Verify the base-layer-luminance actually changed (this drives the visual theme)
        var toggledLuminance = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.documentElement).getPropertyValue('--base-layer-luminance').trim()");
        Assert.NotEqual(initialLuminance, toggledLuminance);

        // Toggle back and verify it reverts
        await themeSwitch.ClickAsync();
        await page.WaitForTimeoutAsync(1500);

        var revertedLuminance = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.documentElement).getPropertyValue('--base-layer-luminance').trim()");
        Assert.Equal(initialLuminance, revertedLuminance);
    }

    [Fact]
    public async Task NavigationSidebar_HasExpectedLinks()
    {
        var page = await _fixture.CreatePageAsync();
        await page.GotoAsync($"{_fixture.BaseUrl}/", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 30000
        });

        await page.WaitForTimeoutAsync(2000);

        // Icon sidebar uses NavLink which renders as <a> elements
        var navLinks = page.Locator("nav.icon-sidebar a[href]");
        var count = await navLinks.CountAsync();
        Assert.True(count >= 3, $"Expected at least 3 nav links, found {count}");
    }

    [Fact]
    public async Task RootPage_LoadsInspector()
    {
        var page = await _fixture.CreatePageAsync();
        await page.GotoAsync($"{_fixture.BaseUrl}/", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 30000
        });

        await page.WaitForTimeoutAsync(2000);

        var table = page.Locator(".inspection-table");
        await table.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        var isVisible = await table.IsVisibleAsync();
        Assert.True(isVisible);
    }
}
