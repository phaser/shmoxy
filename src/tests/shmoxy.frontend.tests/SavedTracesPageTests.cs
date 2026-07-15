using System.Net.Http.Json;
using Microsoft.Playwright;
using shmoxy.frontend.models;
using Xunit;

namespace shmoxy.frontend.tests;

[Collection("Frontend")]
public class SavedTracesPageTests
{
    private readonly FrontendTestFixture _fixture;

    public SavedTracesPageTests(FrontendTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SavedTracesPage_HasCorrectColumns()
    {
        var page = await _fixture.CreatePageAsync();
        await page.GotoAsync($"{_fixture.BaseUrl}/saved-traces", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 30000
        });

        await page.WaitForTimeoutAsync(2000);

        var headers = page.Locator(".saved-traces-table th");
        var count = await headers.CountAsync();
        Assert.Equal(10, count);

        var headerTexts = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var text = await headers.Nth(i).InnerTextAsync();
            headerTexts.Add(text.Trim());
        }

        Assert.Equal("", headerTexts[0]);
        Assert.Equal("Saved at", headerTexts[1]);
        Assert.Equal("Captured at", headerTexts[2]);
        Assert.Equal("Method", headerTexts[3]);
        Assert.Equal("URL", headerTexts[4]);
        Assert.Equal("Status", headerTexts[5]);
        Assert.Equal("Size", headerTexts[6]);
        Assert.Equal("Duration", headerTexts[7]);
        Assert.Equal("Note", headerTexts[8]);
    }

    [Fact]
    public async Task SavedTracesPage_SidebarLinkNavigates()
    {
        var page = await _fixture.CreatePageAsync();
        await page.GotoAsync($"{_fixture.BaseUrl}/", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 30000
        });
        await page.WaitForTimeoutAsync(1000);

        await page.ClickAsync(".icon-sidebar a[href='/saved-traces']");
        await page.WaitForSelectorAsync(".saved-traces-table", new PageWaitForSelectorOptions { Timeout = 10000 });
    }

    [Fact]
    public async Task SavedTracesPage_BrowseAnnotateUnsave_Flow()
    {
        var marker = Guid.NewGuid().ToString("N");
        var url = $"https://e2e-test.example.com/{marker}";
        using var client = new HttpClient { BaseAddress = new Uri(_fixture.BaseUrl) };

        // Seed a saved trace through the API (stands in for the Inspection-page save toggle)
        var saveResponse = await client.PostAsJsonAsync("/api/saved-traces", new SavedTraceData
        {
            Method = "GET",
            Url = url,
            StatusCode = 200,
            DurationMs = 42,
            Timestamp = DateTime.UtcNow,
            RequestHeaders = [new("Host", "e2e-test.example.com")],
            ResponseBody = "{\"e2e\":true}"
        });
        saveResponse.EnsureSuccessStatusCode();
        var summary = await saveResponse.Content.ReadFromJsonAsync<SavedTraceSummary>();
        Assert.NotNull(summary);

        try
        {
            var page = await _fixture.CreatePageAsync();
            await page.GotoAsync($"{_fixture.BaseUrl}/saved-traces", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 30000
            });
            await page.WaitForTimeoutAsync(2000);

            // The seeded trace is the newest, so it renders at the top of the table
            var row = page.Locator(".saved-traces-table tbody tr").Filter(new LocatorFilterOptions { HasText = marker });
            await row.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });

            // Open the detail panel — it must show the trace as saved
            await row.ClickAsync();
            await page.WaitForSelectorAsync(".detail-overlay", new PageWaitForSelectorOptions { Timeout = 10000 });
            var saveButton = page.Locator(".save-trace-button");
            Assert.Contains("Saved", await saveButton.InnerTextAsync());

            // Add a note from the detail panel
            await page.FillAsync(".note-textarea", "e2e test note");
            await page.ClickAsync(".note-save-button");
            await page.WaitForTimeoutAsync(1000);

            var afterNote = await client.GetFromJsonAsync<List<SavedTraceSummary>>("/api/saved-traces");
            Assert.Equal("e2e test note", afterNote!.Single(t => t.Id == summary.Id).Note);

            // Close the detail and unsave from the table
            await page.ClickAsync(".detail-panel .close-button");
            await page.WaitForTimeoutAsync(500);

            await row.Locator(".unsave-button").ClickAsync();
            await page.WaitForTimeoutAsync(1500);

            var afterUnsave = await client.GetFromJsonAsync<List<SavedTraceSummary>>("/api/saved-traces");
            Assert.DoesNotContain(afterUnsave!, t => t.Id == summary.Id);
            Assert.Equal(0, await page.Locator(".saved-traces-table tbody tr").Filter(new LocatorFilterOptions { HasText = marker }).CountAsync());
        }
        finally
        {
            // Clean up if an assertion failed before the unsave step
            await client.DeleteAsync($"/api/saved-traces/{summary.Id}");
        }
    }

    [Fact]
    public async Task SavedTracesPage_SearchFiltersRows()
    {
        var marker = Guid.NewGuid().ToString("N");
        using var client = new HttpClient { BaseAddress = new Uri(_fixture.BaseUrl) };

        var saveResponse = await client.PostAsJsonAsync("/api/saved-traces", new SavedTraceData
        {
            Method = "GET",
            Url = $"https://search-test.example.com/{marker}",
            StatusCode = 200,
            Timestamp = DateTime.UtcNow
        });
        saveResponse.EnsureSuccessStatusCode();
        var summary = await saveResponse.Content.ReadFromJsonAsync<SavedTraceSummary>();

        try
        {
            var page = await _fixture.CreatePageAsync();
            await page.GotoAsync($"{_fixture.BaseUrl}/saved-traces", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 30000
            });
            await page.WaitForTimeoutAsync(2000);

            // FluentTextField commits its value on change/blur, not on keystrokes
            var searchInput = page.Locator(".filters fluent-text-field input");
            await searchInput.FillAsync("no-trace-matches-this-query");
            await searchInput.BlurAsync();
            await page.WaitForTimeoutAsync(500);

            var emptyState = page.Locator(".saved-traces-table .empty-state");
            Assert.Contains("match the search", await emptyState.InnerTextAsync());

            await searchInput.FillAsync(marker);
            await searchInput.BlurAsync();
            await page.WaitForTimeoutAsync(500);

            var rows = page.Locator(".saved-traces-table tbody tr.clickable-row");
            Assert.Equal(1, await rows.CountAsync());
        }
        finally
        {
            await client.DeleteAsync($"/api/saved-traces/{summary!.Id}");
        }
    }

    [Fact]
    public async Task SavedTracesPage_CompareTwoTraces_Flow()
    {
        var marker = Guid.NewGuid().ToString("N");
        using var client = new HttpClient { BaseAddress = new Uri(_fixture.BaseUrl) };

        // Seed two captures of the same endpoint with differing query/status/body/headers
        var older = await SeedTrace(client, new SavedTraceData
        {
            Method = "GET",
            Url = $"https://compare-test.example.com/{marker}?page=1&size=10",
            StatusCode = 200,
            DurationMs = 100,
            Timestamp = DateTime.UtcNow.AddMinutes(-10),
            RequestHeaders = [new("Host", "compare-test.example.com"), new("Accept", "application/json")],
            ResponseHeaders = [new("Content-Type", "application/json")],
            ResponseBody = "{\"status\":\"ok\",\"count\":1}",
            ResponseContentType = "application/json"
        });
        var newer = await SeedTrace(client, new SavedTraceData
        {
            Method = "GET",
            Url = $"https://compare-test.example.com/{marker}?page=2&filter=active",
            StatusCode = 500,
            DurationMs = 250,
            Timestamp = DateTime.UtcNow,
            RequestHeaders = [new("Host", "compare-test.example.com"), new("Authorization", "Bearer x")],
            ResponseHeaders = [new("Content-Type", "application/json")],
            ResponseBody = "{\"status\":\"error\",\"count\":1}",
            ResponseContentType = "application/json"
        });

        try
        {
            var page = await _fixture.CreatePageAsync();
            await page.GotoAsync($"{_fixture.BaseUrl}/saved-traces", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 30000
            });
            await page.WaitForTimeoutAsync(2000);

            // Compare is disabled until exactly two traces are selected
            var compareButton = page.Locator("fluent-button", new PageLocatorOptions { HasText = "Compare" });
            Assert.NotNull(await compareButton.GetAttributeAsync("disabled"));

            var rows = page.Locator(".saved-traces-table tbody tr").Filter(new LocatorFilterOptions { HasText = marker });
            await rows.First.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
            Assert.Equal(2, await rows.CountAsync());

            await rows.Nth(0).Locator(".compare-checkbox").CheckAsync();
            await rows.Nth(1).Locator(".compare-checkbox").CheckAsync();
            await page.WaitForTimeoutAsync(500);

            Assert.Null(await compareButton.GetAttributeAsync("disabled"));
            await compareButton.ClickAsync();

            await page.WaitForSelectorAsync(".compare-overlay", new PageWaitForSelectorOptions { Timeout = 10000 });

            // Structural URL diff: page changed, size removed, filter added
            var queryTable = page.Locator(".compare-overlay .align-table").First;
            Assert.Contains("page", await queryTable.InnerTextAsync());

            // Header diff shows the added Authorization header
            Assert.Contains("Authorization", await page.Locator(".compare-overlay").InnerTextAsync());

            // Body diff renders side-by-side rows with a changed line
            var changedRows = page.Locator(".compare-overlay .line-diff-table tr.row-changed");
            Assert.True(await changedRows.CountAsync() > 0);

            // Swap sides still renders
            await page.ClickAsync(".swap-button");
            await page.WaitForTimeoutAsync(500);
            Assert.True(await page.Locator(".compare-overlay").IsVisibleAsync());

            // Close via the × button
            await page.ClickAsync(".compare-overlay .close-button");
            await page.WaitForTimeoutAsync(500);
            Assert.Equal(0, await page.Locator(".compare-overlay").CountAsync());
        }
        finally
        {
            await client.DeleteAsync($"/api/saved-traces/{older}");
            await client.DeleteAsync($"/api/saved-traces/{newer}");
        }
    }

    private static async Task<string> SeedTrace(HttpClient client, SavedTraceData trace)
    {
        var response = await client.PostAsJsonAsync("/api/saved-traces", trace);
        response.EnsureSuccessStatusCode();
        var summary = await response.Content.ReadFromJsonAsync<SavedTraceSummary>();
        return summary!.Id;
    }
}
