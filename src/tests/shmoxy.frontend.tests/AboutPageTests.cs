using Microsoft.Playwright;
using Xunit;

namespace shmoxy.frontend.tests;

[Collection("Frontend")]
public class AboutPageTests
{
    private readonly FrontendTestFixture _fixture;

    public AboutPageTests(FrontendTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AboutPage_ShowsVersionNumber()
    {
        var page = await _fixture.CreatePageAsync();
        await page.GotoAsync($"{_fixture.BaseUrl}/about", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 30000
        });

        await page.WaitForTimeoutAsync(3000);

        var content = await page.TextContentAsync(".about-container");
        Assert.Contains("Version", content);
    }

    [Fact]
    public async Task AboutPage_ShowsMitLicense()
    {
        var page = await _fixture.CreatePageAsync();
        await page.GotoAsync($"{_fixture.BaseUrl}/about", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 30000
        });

        await page.WaitForTimeoutAsync(3000);

        var licenseLink = page.Locator("a[href*='LICENSE']");
        await licenseLink.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        var isVisible = await licenseLink.IsVisibleAsync();
        Assert.True(isVisible, "Expected MIT License link to be visible");

        var linkText = await licenseLink.TextContentAsync();
        Assert.Contains("MIT License", linkText);
    }

    [Fact]
    public async Task AboutPage_ShowsThirdPartyTable_WithCyberChef()
    {
        var page = await _fixture.CreatePageAsync();
        await page.GotoAsync($"{_fixture.BaseUrl}/about", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 30000
        });

        await page.WaitForTimeoutAsync(3000);

        var table = page.Locator(".about-container table");
        await table.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        var isVisible = await table.IsVisibleAsync();
        Assert.True(isVisible, "Expected third-party table to be visible");

        var tableContent = await table.TextContentAsync();
        Assert.Contains("CyberChef", tableContent);
        Assert.Contains("Apache 2.0", tableContent);
    }

    [Fact]
    public async Task AboutPage_IsAccessibleFromSidebar()
    {
        var page = await _fixture.CreatePageAsync();
        await page.GotoAsync($"{_fixture.BaseUrl}/", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 30000
        });

        await page.WaitForTimeoutAsync(3000);

        var aboutLink = page.Locator("nav.icon-sidebar a[href='/about']");
        await aboutLink.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        var isVisible = await aboutLink.IsVisibleAsync();
        Assert.True(isVisible, "Expected About nav item to be visible in sidebar");

        await aboutLink.ClickAsync();
        await page.WaitForURLAsync("**/about", new PageWaitForURLOptions { Timeout = 10000 });

        var heading = page.Locator("h1");
        var headingText = await heading.TextContentAsync();
        Assert.Equal("About", headingText);
    }
}
