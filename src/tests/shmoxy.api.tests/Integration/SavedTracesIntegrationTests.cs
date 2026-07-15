using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using shmoxy.api.data;
using shmoxy.api.models.dto;

namespace shmoxy.api.tests.Integration;

/// <summary>
/// Full HTTP round-trip for saved traces against a real SQLite database:
/// save, list, get, update note, delete — including WebSocket frame
/// snapshots and cascade delete.
/// </summary>
public class SavedTracesIntegrationTests : IDisposable
{
    private readonly string _dbPath;

    public SavedTracesIntegrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"shmoxy-test-{Guid.NewGuid()}.db");
    }

    private WebApplicationFactory<Program> CreateFactory()
    {
        var connectionString = $"Data Source={_dbPath}";
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ApiConfig:AutoStartProxy"] = "false",
                    ["ApiConfig:ConnectionString"] = connectionString
                });
            });
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<ProxiesDbContext>));
                if (descriptor != null)
                    services.Remove(descriptor);

                services.AddDbContext<ProxiesDbContext>(options =>
                    options.UseSqlite(connectionString));
            });
        });
    }

    [Fact]
    public async Task SavedTrace_FullLifecycle_Succeeds()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        // Save
        var trace = new SavedTraceDto
        {
            Method = "POST",
            Url = "https://example.com/api/submit",
            StatusCode = 201,
            DurationMs = 120,
            Timestamp = DateTime.UtcNow,
            RequestHeaders = [new("Host", "example.com"), new("Content-Type", "application/json")],
            ResponseHeaders = [new("Content-Type", "application/json")],
            RequestBody = "{\"key\":\"value\"}",
            ResponseBody = "{\"id\":1}"
        };

        var saveResponse = await client.PostAsJsonAsync("/api/saved-traces", trace);
        Assert.Equal(HttpStatusCode.Created, saveResponse.StatusCode);
        var summary = await saveResponse.Content.ReadFromJsonAsync<SavedTraceSummaryDto>();
        Assert.NotNull(summary);
        Assert.NotEmpty(summary.Id);

        // List
        var list = await client.GetFromJsonAsync<List<SavedTraceSummaryDto>>("/api/saved-traces");
        Assert.NotNull(list);
        var listed = Assert.Single(list);
        Assert.Equal(summary.Id, listed.Id);
        Assert.Equal("POST", listed.Method);
        Assert.Equal(trace.ResponseBody!.Length, listed.ResponseBodySize);

        // Get full trace
        var full = await client.GetFromJsonAsync<SavedTraceDto>($"/api/saved-traces/{summary.Id}");
        Assert.NotNull(full);
        Assert.Equal("{\"key\":\"value\"}", full.RequestBody);
        Assert.NotNull(full.RequestHeaders);
        Assert.Contains(full.RequestHeaders, h => h.Key == "Host" && h.Value == "example.com");

        // Update note
        var patchResponse = await client.PatchAsJsonAsync(
            $"/api/saved-traces/{summary.Id}", new UpdateSavedTraceNoteRequest { Note = "suspicious payload" });
        Assert.Equal(HttpStatusCode.NoContent, patchResponse.StatusCode);

        var afterNote = await client.GetFromJsonAsync<List<SavedTraceSummaryDto>>("/api/saved-traces");
        Assert.Equal("suspicious payload", afterNote!.Single().Note);

        // Delete
        var deleteResponse = await client.DeleteAsync($"/api/saved-traces/{summary.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var afterDelete = await client.GetFromJsonAsync<List<SavedTraceSummaryDto>>("/api/saved-traces");
        Assert.Empty(afterDelete!);
    }

    [Fact]
    public async Task SavedTrace_WithWebSocketFrames_RoundTripsAndCascadesOnDelete()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var trace = new SavedTraceDto
        {
            Method = "GET",
            Url = "wss://example.com/socket",
            IsWebSocket = true,
            WebSocketClosed = true,
            Timestamp = DateTime.UtcNow,
            WebSocketFrames =
            [
                new WebSocketFrameDto { Direction = "client", FrameType = "text", Payload = "ping", Timestamp = DateTime.UtcNow },
                new WebSocketFrameDto { Direction = "server", FrameType = "text", Payload = "pong", Timestamp = DateTime.UtcNow.AddMilliseconds(5) }
            ]
        };

        var saveResponse = await client.PostAsJsonAsync("/api/saved-traces", trace);
        Assert.Equal(HttpStatusCode.Created, saveResponse.StatusCode);
        var summary = await saveResponse.Content.ReadFromJsonAsync<SavedTraceSummaryDto>();

        var full = await client.GetFromJsonAsync<SavedTraceDto>($"/api/saved-traces/{summary!.Id}");
        Assert.NotNull(full!.WebSocketFrames);
        Assert.Equal(2, full.WebSocketFrames.Count);
        Assert.Equal("ping", full.WebSocketFrames[0].Payload);
        Assert.Equal("pong", full.WebSocketFrames[1].Payload);

        var deleteResponse = await client.DeleteAsync($"/api/saved-traces/{summary.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // Cascade delete: no orphaned frames remain
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProxiesDbContext>();
        Assert.Equal(0, await db.SavedTraceWebSocketFrames.CountAsync());
    }

    [Fact]
    public async Task GetTrace_ReturnsNotFound_ForUnknownId()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/saved-traces/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            SqliteConnection.ClearAllPools();
            File.Delete(_dbPath);
        }
    }
}
