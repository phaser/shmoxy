using Microsoft.Playwright;
using Xunit;

namespace shmoxy.frontend.tests;

[Collection("Frontend")]
public class CyberChefPageTests
{
    private readonly FrontendTestFixture _fixture;

    public CyberChefPageTests(FrontendTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CyberChefPage_ShowsDisabledMessage_WhenNotEnabled()
    {
        var page = await _fixture.CreatePageAsync();
        await page.GotoAsync($"{_fixture.BaseUrl}/cyberchef", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 30000
        });

        await page.WaitForTimeoutAsync(3000);

        var disabledMessage = page.Locator(".cyberchef-disabled");
        await disabledMessage.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        var isVisible = await disabledMessage.IsVisibleAsync();
        Assert.True(isVisible, "Expected disabled message to be visible when CyberChef is not enabled");

        var settingsLink = page.Locator(".disabled-message a[href='/settings']");
        var linkVisible = await settingsLink.IsVisibleAsync();
        Assert.True(linkVisible, "Expected a link to Settings page");
    }

    [Fact]
    public async Task CyberChefPage_ShowsIframe_WhenEnabled()
    {
        var page = await _fixture.CreatePageAsync();

        // Enable CyberChef via localStorage before navigating
        await page.GotoAsync($"{_fixture.BaseUrl}/settings", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 30000
        });

        await page.EvaluateAsync("() => localStorage.setItem('shmoxy-enable-cyberchef', 'True')");

        await page.GotoAsync($"{_fixture.BaseUrl}/cyberchef", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 30000
        });

        await page.WaitForTimeoutAsync(3000);

        var iframe = page.Locator("iframe.cyberchef-iframe");
        await iframe.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        var isVisible = await iframe.IsVisibleAsync();
        Assert.True(isVisible, "Expected CyberChef iframe to be visible when enabled");

        // Clean up
        await page.EvaluateAsync("() => localStorage.removeItem('shmoxy-enable-cyberchef')");
    }
}
