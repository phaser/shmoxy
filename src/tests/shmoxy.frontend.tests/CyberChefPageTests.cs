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

    [Fact]
    public async Task CyberChefPage_LoadsCyberChefContent_WhenEnabled()
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

        // Wait for the iframe to appear
        var iframe = page.Locator("iframe.cyberchef-iframe");
        await iframe.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });

        // Get the iframe's content frame and verify CyberChef actually loaded
        var frame = page.FrameLocator("iframe.cyberchef-iframe");

        // CyberChef has an element with id "input-text" for the input area
        // and the page title contains "CyberChef"
        var inputArea = frame.Locator("#input-text,#input-wrapper,[id*='input']").First;
        await inputArea.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        var inputVisible = await inputArea.IsVisibleAsync();
        Assert.True(inputVisible, "Expected CyberChef input area to be visible inside iframe - CyberChef content did not load");

        // Clean up
        await page.EvaluateAsync("() => localStorage.removeItem('shmoxy-enable-cyberchef')");
    }
}
