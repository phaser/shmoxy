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
        Assert.Equal(5, count);

        var headerTexts = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var text = await headers.Nth(i).InnerTextAsync();
            headerTexts.Add(text.Trim());
        }

        Assert.Equal("#", headerTexts[0]);
        Assert.Equal("Method", headerTexts[1]);
        Assert.Equal("URL", headerTexts[2]);
        Assert.Equal("Status", headerTexts[3]);
        Assert.Equal("Duration", headerTexts[4]);
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
