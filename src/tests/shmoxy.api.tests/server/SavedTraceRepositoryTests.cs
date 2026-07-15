using Microsoft.EntityFrameworkCore;
using shmoxy.api.data;
using shmoxy.api.models;
using shmoxy.api.server;

namespace shmoxy.api.tests.server;

public class SavedTraceRepositoryTests : IDisposable
{
    private readonly ProxiesDbContext _dbContext;
    private readonly SavedTraceRepository _repository;

    public SavedTraceRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<ProxiesDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ProxiesDbContext(options);
        _dbContext.Database.EnsureCreated();
        _repository = new SavedTraceRepository(_dbContext);
    }

    [Fact]
    public async Task SaveAsync_PersistsTrace_AndSetsSavedAt()
    {
        var before = DateTime.UtcNow;

        var saved = await _repository.SaveAsync(new SavedTrace
        {
            Method = "GET",
            Url = "https://example.com",
            StatusCode = 200,
            Timestamp = DateTime.UtcNow
        });

        Assert.Equal(1, await _dbContext.SavedTraces.CountAsync());
        Assert.InRange(saved.SavedAt, before, DateTime.UtcNow);
    }

    [Fact]
    public async Task SaveAsync_PersistsWebSocketFrames()
    {
        var trace = new SavedTrace { Method = "GET", Url = "wss://example.com", IsWebSocket = true };
        trace.WebSocketFrames.Add(new SavedTraceWebSocketFrame
        {
            SavedTraceId = trace.Id,
            Direction = "client",
            FrameType = "text",
            Payload = "ping",
            Timestamp = DateTime.UtcNow
        });

        await _repository.SaveAsync(trace);

        Assert.Equal(1, await _dbContext.SavedTraceWebSocketFrames.CountAsync());
    }

    [Fact]
    public async Task ListAsync_ReturnsOrderedBySavedAtDescending()
    {
        _dbContext.SavedTraces.Add(new SavedTrace { Method = "GET", Url = "https://older", SavedAt = DateTime.UtcNow.AddHours(-1) });
        _dbContext.SavedTraces.Add(new SavedTrace { Method = "GET", Url = "https://newer", SavedAt = DateTime.UtcNow });
        await _dbContext.SaveChangesAsync();

        var summaries = await _repository.ListAsync();

        Assert.Equal(2, summaries.Count);
        Assert.Equal("https://newer", summaries[0].Url);
        Assert.Equal("https://older", summaries[1].Url);
    }

    [Fact]
    public async Task ListAsync_ComputesResponseBodySize()
    {
        _dbContext.SavedTraces.Add(new SavedTrace { Method = "GET", Url = "https://text", ResponseBody = "12345" });
        _dbContext.SavedTraces.Add(new SavedTrace { Method = "GET", Url = "https://binary", ResponseBodyBase64 = Convert.ToBase64String(new byte[9]) });
        _dbContext.SavedTraces.Add(new SavedTrace { Method = "GET", Url = "https://empty" });
        await _dbContext.SaveChangesAsync();

        var summaries = await _repository.ListAsync();

        Assert.Equal(5, summaries.Single(s => s.Url == "https://text").ResponseBodySize);
        Assert.Equal(9, summaries.Single(s => s.Url == "https://binary").ResponseBodySize);
        Assert.Null(summaries.Single(s => s.Url == "https://empty").ResponseBodySize);
    }

    [Fact]
    public async Task ListAsync_IncludesNote()
    {
        _dbContext.SavedTraces.Add(new SavedTrace { Method = "GET", Url = "https://a", Note = "check auth header" });
        await _dbContext.SaveChangesAsync();

        var summaries = await _repository.ListAsync();

        Assert.Equal("check auth header", summaries[0].Note);
    }

    [Fact]
    public async Task GetAsync_ReturnsTraceWithFrames()
    {
        var trace = new SavedTrace { Method = "GET", Url = "wss://example.com", IsWebSocket = true };
        trace.WebSocketFrames.Add(new SavedTraceWebSocketFrame
        {
            SavedTraceId = trace.Id,
            Direction = "server",
            FrameType = "text",
            Payload = "pong",
            Timestamp = DateTime.UtcNow
        });
        _dbContext.SavedTraces.Add(trace);
        await _dbContext.SaveChangesAsync();

        var retrieved = await _repository.GetAsync(trace.Id);

        Assert.NotNull(retrieved);
        Assert.Single(retrieved.WebSocketFrames);
        Assert.Equal("pong", retrieved.WebSocketFrames[0].Payload);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenMissing()
    {
        var retrieved = await _repository.GetAsync("missing");

        Assert.Null(retrieved);
    }

    [Fact]
    public async Task UpdateNoteAsync_SetsNote()
    {
        var trace = new SavedTrace { Method = "GET", Url = "https://example.com" };
        _dbContext.SavedTraces.Add(trace);
        await _dbContext.SaveChangesAsync();

        var updated = await _repository.UpdateNoteAsync(trace.Id, "interesting request");

        Assert.True(updated);
        Assert.Equal("interesting request", (await _dbContext.SavedTraces.FindAsync(trace.Id))!.Note);
    }

    [Fact]
    public async Task UpdateNoteAsync_ClearsNote_WhenEmptyOrWhitespace()
    {
        var trace = new SavedTrace { Method = "GET", Url = "https://example.com", Note = "old note" };
        _dbContext.SavedTraces.Add(trace);
        await _dbContext.SaveChangesAsync();

        var updated = await _repository.UpdateNoteAsync(trace.Id, "  ");

        Assert.True(updated);
        Assert.Null((await _dbContext.SavedTraces.FindAsync(trace.Id))!.Note);
    }

    [Fact]
    public async Task UpdateNoteAsync_ReturnsFalse_WhenMissing()
    {
        var updated = await _repository.UpdateNoteAsync("missing", "note");

        Assert.False(updated);
    }

    [Fact]
    public async Task DeleteAsync_RemovesTrace()
    {
        var trace = new SavedTrace { Method = "GET", Url = "https://example.com" };
        _dbContext.SavedTraces.Add(trace);
        await _dbContext.SaveChangesAsync();

        var deleted = await _repository.DeleteAsync(trace.Id);

        Assert.True(deleted);
        Assert.Equal(0, await _dbContext.SavedTraces.CountAsync());
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFalse_WhenMissing()
    {
        var deleted = await _repository.DeleteAsync("missing");

        Assert.False(deleted);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
