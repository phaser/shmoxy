using Microsoft.Playwright;
using Xunit;

namespace shmoxy.frontend.tests.pages;

[Collection("Frontend")]
public class SettingsPageTests
{
    private readonly FrontendTestFixture _fixture;

    public SettingsPageTests(FrontendTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SettingsPage_DoesNotShowSettingsHeader()
    {
        var page = await _fixture.CreatePageAsync();
        await page.GotoAsync($"{_fixture.BaseUrl}/settings", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 30000
        });

        await page.WaitForTimeoutAsync(3000);

        var h1 = page.Locator("h1");
        var count = await h1.CountAsync();
        if (count > 0)
        {
            var text = await h1.TextContentAsync();
            Assert.DoesNotContain("Settings", text);
        }
    }

    [Fact]
    public async Task SettingsPage_HasAccordionLayout()
    {
        var page = await _fixture.CreatePageAsync();
        await page.GotoAsync($"{_fixture.BaseUrl}/settings", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 30000
        });

        await page.WaitForTimeoutAsync(3000);

        var accordion = page.Locator("fluent-accordion");
        await accordion.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        var isVisible = await accordion.IsVisibleAsync();
        Assert.True(isVisible, "Expected accordion component to be visible on settings page");
    }

    [Fact]
    public async Task SettingsPage_HasAllSettingSections()
    {
        var page = await _fixture.CreatePageAsync();
        await page.GotoAsync($"{_fixture.BaseUrl}/settings", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 30000
        });

        await page.WaitForTimeoutAsync(3000);

        var items = page.Locator("fluent-accordion-item");
        var count = await items.CountAsync();
        Assert.Equal(4, count);

        var content = await page.Locator("fluent-accordion").TextContentAsync();
        Assert.Contains("Appearance", content);
        Assert.Contains("Tools", content);
        Assert.Contains("Security", content);
        Assert.Contains("Session Retention", content);
    }

    [Fact]
    public async Task SettingsPage_AccordionItemExpandsOnClick()
    {
        var page = await _fixture.CreatePageAsync();
        await page.GotoAsync($"{_fixture.BaseUrl}/settings", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 30000
        });

        // Wait for Blazor SignalR circuit to connect
        await page.WaitForTimeoutAsync(5000);

        // Click on the second accordion item (Tools) heading to expand it
        var toolsItem = page.Locator("fluent-accordion-item").Nth(1);
        await toolsItem.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        // The expanded item should contain the CyberChef toggle
        var expanded = await toolsItem.GetAttributeAsync("expanded");
        Assert.NotNull(expanded);
    }
}
