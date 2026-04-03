using Microsoft.Playwright;
using Xunit;

namespace shmoxy.frontend.tests;

[Collection("Frontend")]
public class InspectionPageTests
{
    private readonly FrontendTestFixture _fixture;

    public InspectionPageTests(FrontendTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task InspectionPage_HasCorrectColumns()
    {
        var page = await _fixture.CreatePageAsync();
        await page.GotoAsync($"{_fixture.BaseUrl}/inspection", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 30000
        });

        await page.WaitForTimeoutAsync(2000);

        var headers = page.Locator(".inspection-table th");
        var count = await headers.CountAsync();
        Assert.Equal(7, count);

        var headerTexts = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var text = await headers.Nth(i).InnerTextAsync();
            headerTexts.Add(text.Trim());
        }

        Assert.Equal("#", headerTexts[0]);
        Assert.Equal("Time", headerTexts[1]);
        Assert.Equal("Method", headerTexts[2]);
        Assert.Equal("URL", headerTexts[3]);
        Assert.Equal("Status", headerTexts[4]);
        Assert.Equal("Size", headerTexts[5]);
        Assert.Equal("Duration", headerTexts[6]);
    }

    [Fact]
    public async Task InspectionPage_DisposeAsync_NoErrorsOnNavAway()
    {
        var page = await _fixture.CreatePageAsync();

        // Collect console errors during the test
        var consoleErrors = new List<string>();
        page.Console += (_, msg) =>
        {
            if (msg.Type == "error")
                consoleErrors.Add(msg.Text);
        };

        await page.GotoAsync($"{_fixture.BaseUrl}/inspection", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 30000
        });
        await page.WaitForTimeoutAsync(2000);

        // Navigate away to trigger DisposeAsync
        await page.GotoAsync($"{_fixture.BaseUrl}/", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 30000
        });
        await page.WaitForTimeoutAsync(1000);

        // Navigate back to verify inspection page still works
        await page.GotoAsync($"{_fixture.BaseUrl}/inspection", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 30000
        });
        await page.WaitForTimeoutAsync(2000);

        var container = page.Locator("#inspection-scroll-container");
        await container.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });

        // Verify no JS interop errors in the console
        Assert.DoesNotContain(consoleErrors,
            e => e.Contains("InvalidOperationException") || e.Contains("JavaScript interop"));
    }

    [Fact]
    public async Task InspectionPage_HasScrollableContainer()
    {
        var page = await _fixture.CreatePageAsync();
        await page.GotoAsync($"{_fixture.BaseUrl}/inspection", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 30000
        });

        await page.WaitForTimeoutAsync(2000);

        var container = page.Locator("#inspection-scroll-container");
        await container.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });

        var overflowY = await container.EvaluateAsync<string>(
            "el => getComputedStyle(el).overflowY");
        Assert.Equal("auto", overflowY);
    }
}
