using Microsoft.EntityFrameworkCore;
using shmoxy.api.data;
using shmoxy.api.models;
using shmoxy.api.server;

namespace shmoxy.api.tests.server;

public class SessionRepositoryTests : IDisposable
{
    private readonly ProxiesDbContext _dbContext;
    private readonly SessionRepository _repository;

    public SessionRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<ProxiesDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ProxiesDbContext(options);
        _dbContext.Database.EnsureCreated();
        _repository = new SessionRepository(_dbContext);
    }

    [Fact]
    public async Task CreateSessionAsync_CreatesSessionWithRows()
    {
        var rows = new List<InspectionSessionRow>
        {
            new() { Method = "GET", Url = "https://example.com/1", Timestamp = DateTime.UtcNow },
            new() { Method = "POST", Url = "https://example.com/2", Timestamp = DateTime.UtcNow }
        };

        var session = await _repository.CreateSessionAsync("Test Session", rows);

        Assert.Equal("Test Session", session.Name);
        Assert.Equal(2, session.RowCount);
        Assert.Equal(2, await _dbContext.InspectionSessionRows.CountAsync());
    }

    [Fact]
    public async Task CreateSessionAsync_SetsSessionIdOnRows()
    {
        var rows = new List<InspectionSessionRow>
        {
            new() { Method = "GET", Url = "https://example.com", Timestamp = DateTime.UtcNow }
        };

        var session = await _repository.CreateSessionAsync("Test", rows);

        var savedRows = await _dbContext.InspectionSessionRows.ToListAsync();
        Assert.All(savedRows, r => Assert.Equal(session.Id, r.SessionId));
    }

    [Fact]
    public async Task ListSessionsAsync_ReturnsOrderedByUpdatedAtDescending()
    {
        var older = new InspectionSession { Name = "Older", UpdatedAt = DateTime.UtcNow.AddHours(-1) };
        var newer = new InspectionSession { Name = "Newer", UpdatedAt = DateTime.UtcNow };

        _dbContext.InspectionSessions.AddRange(older, newer);
        await _dbContext.SaveChangesAsync();

        var sessions = await _repository.ListSessionsAsync();

        Assert.Equal(2, sessions.Count);
        Assert.Equal("Newer", sessions[0].Name);
        Assert.Equal("Older", sessions[1].Name);
    }

    [Fact]
    public async Task GetSessionAsync_ReturnsSession()
    {
        var session = new InspectionSession { Name = "Find Me" };
        _dbContext.InspectionSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var retrieved = await _repository.GetSessionAsync(session.Id);

        Assert.NotNull(retrieved);
        Assert.Equal("Find Me", retrieved.Name);
    }

    [Fact]
    public async Task GetSessionAsync_ReturnsNullForMissing()
    {
        var result = await _repository.GetSessionAsync("nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public async Task LoadRowsAsync_ReturnsRowsOrderedByTimestamp()
    {
        var session = new InspectionSession { Name = "Test", RowCount = 2 };
        var later = new InspectionSessionRow
        {
            SessionId = session.Id,
            Method = "POST",
            Url = "https://example.com/2",
            Timestamp = DateTime.UtcNow.AddSeconds(1)
        };
        var earlier = new InspectionSessionRow
        {
            SessionId = session.Id,
            Method = "GET",
            Url = "https://example.com/1",
            Timestamp = DateTime.UtcNow
        };

        _dbContext.InspectionSessions.Add(session);
        _dbContext.InspectionSessionRows.AddRange(later, earlier);
        await _dbContext.SaveChangesAsync();

        var rows = await _repository.LoadRowsAsync(session.Id);

        Assert.Equal(2, rows.Count);
        Assert.Equal("GET", rows[0].Method);
        Assert.Equal("POST", rows[1].Method);
    }

    [Fact]
    public async Task LoadRowsAsync_ReturnsEmptyForMissingSession()
    {
        var rows = await _repository.LoadRowsAsync("nonexistent");
        Assert.Empty(rows);
    }

    [Fact]
    public async Task UpdateSessionAsync_ReplacesRows()
    {
        var rows = new List<InspectionSessionRow>
        {
            new() { Method = "GET", Url = "https://example.com/old", Timestamp = DateTime.UtcNow }
        };

        var session = await _repository.CreateSessionAsync("Test", rows);

        var newRows = new List<InspectionSessionRow>
        {
            new() { Method = "POST", Url = "https://example.com/new1", Timestamp = DateTime.UtcNow },
            new() { Method = "PUT", Url = "https://example.com/new2", Timestamp = DateTime.UtcNow }
        };

        await _repository.UpdateSessionAsync(session.Id, newRows);

        var loadedRows = await _repository.LoadRowsAsync(session.Id);
        Assert.Equal(2, loadedRows.Count);
        Assert.Contains(loadedRows, r => r.Method == "POST");
        Assert.Contains(loadedRows, r => r.Method == "PUT");

        var updatedSession = await _repository.GetSessionAsync(session.Id);
        Assert.Equal(2, updatedSession!.RowCount);
    }

    [Fact]
    public async Task UpdateSessionAsync_ThrowsForMissingSession()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _repository.UpdateSessionAsync("nonexistent", new List<InspectionSessionRow>()));
    }

    [Fact]
    public async Task DeleteSessionAsync_RemovesSessionAndRows()
    {
        var rows = new List<InspectionSessionRow>
        {
            new() { Method = "GET", Url = "https://example.com", Timestamp = DateTime.UtcNow }
        };

        var session = await _repository.CreateSessionAsync("To Delete", rows);

        await _repository.DeleteSessionAsync(session.Id);

        Assert.Equal(0, await _dbContext.InspectionSessions.CountAsync());
        Assert.Equal(0, await _dbContext.InspectionSessionRows.CountAsync());
    }

    [Fact]
    public async Task DeleteSessionAsync_DoesNothingForMissingSession()
    {
        await _repository.DeleteSessionAsync("nonexistent");
        // No exception thrown
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
