using Microsoft.EntityFrameworkCore;
using shmoxy.api.data;
using shmoxy.api.models;

namespace shmoxy.api.tests.data;

public class ProxiesDbContextTests : IDisposable
{
    private readonly ProxiesDbContext _dbContext;

    public ProxiesDbContextTests()
    {
        var options = new DbContextOptionsBuilder<ProxiesDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ProxiesDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    [Fact]
    public async Task InspectionSession_CanBeCreatedAndRetrieved()
    {
        var session = new InspectionSession
        {
            Name = "Test Session",
            RowCount = 0
        };

        _dbContext.InspectionSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var retrieved = await _dbContext.InspectionSessions.FindAsync(session.Id);

        Assert.NotNull(retrieved);
        Assert.Equal("Test Session", retrieved.Name);
        Assert.NotEqual(default, retrieved.CreatedAt);
    }

    [Fact]
    public async Task InspectionSessionRow_CanBeCreatedWithSession()
    {
        var session = new InspectionSession
        {
            Name = "Test Session",
            RowCount = 1
        };

        var row = new InspectionSessionRow
        {
            SessionId = session.Id,
            Method = "GET",
            Url = "https://example.com/api/test",
            StatusCode = 200,
            DurationMs = 150,
            Timestamp = DateTime.UtcNow,
            RequestHeaders = """{"Accept":"application/json"}""",
            ResponseHeaders = """{"Content-Type":"application/json"}""",
            RequestBody = null,
            ResponseBody = """{"status":"ok"}"""
        };

        _dbContext.InspectionSessions.Add(session);
        _dbContext.InspectionSessionRows.Add(row);
        await _dbContext.SaveChangesAsync();

        var retrievedRow = await _dbContext.InspectionSessionRows
            .Include(r => r.Session)
            .FirstAsync(r => r.Id == row.Id);

        Assert.Equal("GET", retrievedRow.Method);
        Assert.Equal("https://example.com/api/test", retrievedRow.Url);
        Assert.Equal(200, retrievedRow.StatusCode);
        Assert.Equal(150, retrievedRow.DurationMs);
        Assert.NotNull(retrievedRow.Session);
        Assert.Equal("Test Session", retrievedRow.Session.Name);
    }

    [Fact]
    public async Task InspectionSession_CascadeDeletesRows()
    {
        var session = new InspectionSession
        {
            Name = "Session To Delete",
            RowCount = 2
        };

        var row1 = new InspectionSessionRow
        {
            SessionId = session.Id,
            Method = "GET",
            Url = "https://example.com/1",
            Timestamp = DateTime.UtcNow
        };

        var row2 = new InspectionSessionRow
        {
            SessionId = session.Id,
            Method = "POST",
            Url = "https://example.com/2",
            Timestamp = DateTime.UtcNow
        };

        _dbContext.InspectionSessions.Add(session);
        _dbContext.InspectionSessionRows.AddRange(row1, row2);
        await _dbContext.SaveChangesAsync();

        Assert.Equal(2, await _dbContext.InspectionSessionRows.CountAsync());

        _dbContext.InspectionSessions.Remove(session);
        await _dbContext.SaveChangesAsync();

        Assert.Equal(0, await _dbContext.InspectionSessions.CountAsync());
        Assert.Equal(0, await _dbContext.InspectionSessionRows.CountAsync());
    }

    [Fact]
    public async Task InspectionSession_LoadsRowsViaNavigation()
    {
        var session = new InspectionSession
        {
            Name = "Session With Rows",
            RowCount = 2
        };

        session.Rows.Add(new InspectionSessionRow
        {
            SessionId = session.Id,
            Method = "GET",
            Url = "https://example.com/a",
            Timestamp = DateTime.UtcNow
        });

        session.Rows.Add(new InspectionSessionRow
        {
            SessionId = session.Id,
            Method = "POST",
            Url = "https://example.com/b",
            Timestamp = DateTime.UtcNow
        });

        _dbContext.InspectionSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var retrieved = await _dbContext.InspectionSessions
            .Include(s => s.Rows)
            .FirstAsync(s => s.Id == session.Id);

        Assert.Equal(2, retrieved.Rows.Count);
    }

    [Fact]
    public async Task InspectionSessionRow_NullableFieldsAreOptional()
    {
        var session = new InspectionSession
        {
            Name = "Minimal Session",
            RowCount = 1
        };

        var row = new InspectionSessionRow
        {
            SessionId = session.Id,
            Method = "GET",
            Url = "https://example.com",
            Timestamp = DateTime.UtcNow,
            StatusCode = null,
            DurationMs = null,
            RequestHeaders = null,
            ResponseHeaders = null,
            RequestBody = null,
            ResponseBody = null
        };

        _dbContext.InspectionSessions.Add(session);
        _dbContext.InspectionSessionRows.Add(row);
        await _dbContext.SaveChangesAsync();

        var retrieved = await _dbContext.InspectionSessionRows.FindAsync(row.Id);

        Assert.NotNull(retrieved);
        Assert.Null(retrieved.StatusCode);
        Assert.Null(retrieved.DurationMs);
        Assert.Null(retrieved.RequestHeaders);
        Assert.Null(retrieved.ResponseBody);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
