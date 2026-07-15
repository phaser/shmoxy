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
        Assert.Equal(9, count);

        var headerTexts = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var text = await headers.Nth(i).InnerTextAsync();
            headerTexts.Add(text.Trim());
        }

        Assert.Equal("Saved at", headerTexts[0]);
        Assert.Equal("Captured at", headerTexts[1]);
        Assert.Equal("Method", headerTexts[2]);
        Assert.Equal("URL", headerTexts[3]);
        Assert.Equal("Status", headerTexts[4]);
        Assert.Equal("Size", headerTexts[5]);
        Assert.Equal("Duration", headerTexts[6]);
        Assert.Equal("Note", headerTexts[7]);
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
}
