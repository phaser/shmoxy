using Microsoft.EntityFrameworkCore;
using shmoxy.api.data;
using shmoxy.api.models;
using shmoxy.api.server;

namespace shmoxy.api.tests.server;

public class SessionRepositoryWebSocketTests : IDisposable
{
    private readonly ProxiesDbContext _dbContext;
    private readonly SessionRepository _repository;

    public SessionRepositoryWebSocketTests()
    {
        var options = new DbContextOptionsBuilder<ProxiesDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ProxiesDbContext(options);
        _dbContext.Database.EnsureCreated();
        _repository = new SessionRepository(_dbContext);
    }

    [Fact]
    public async Task CreateSessionAsync_WithWebSocketRow_PersistsFrames()
    {
        var row = new InspectionSessionRow
        {
            Method = "GET",
            Url = "wss://example.com/ws",
            Timestamp = DateTime.UtcNow,
            IsWebSocket = true,
            WebSocketClosed = false
        };
        row.WebSocketFrames = new List<InspectionSessionWebSocketFrame>
        {
            new()
            {
                SessionRowId = row.Id,
                Timestamp = DateTime.UtcNow,
                Direction = "Client",
                FrameType = "Text",
                Payload = "{\"type\":\"ping\"}"
            },
            new()
            {
                SessionRowId = row.Id,
                Timestamp = DateTime.UtcNow.AddMilliseconds(50),
                Direction = "Server",
                FrameType = "Text",
                Payload = "{\"type\":\"pong\"}"
            }
        };

        var session = await _repository.CreateSessionAsync("WS Test", new List<InspectionSessionRow> { row });

        Assert.Equal(1, session.RowCount);
        Assert.Equal(2, await _dbContext.InspectionSessionWebSocketFrames.CountAsync());
    }

    [Fact]
    public async Task LoadRowsAsync_IncludesWebSocketFrames()
    {
        var row = new InspectionSessionRow
        {
            Method = "GET",
            Url = "wss://example.com/ws",
            Timestamp = DateTime.UtcNow,
            IsWebSocket = true,
            WebSocketClosed = true
        };
        row.WebSocketFrames = new List<InspectionSessionWebSocketFrame>
        {
            new()
            {
                SessionRowId = row.Id,
                Timestamp = DateTime.UtcNow,
                Direction = "Client",
                FrameType = "Text",
                Payload = "hello"
            },
            new()
            {
                SessionRowId = row.Id,
                Timestamp = DateTime.UtcNow.AddMilliseconds(100),
                Direction = "Server",
                FrameType = "Binary",
                Payload = null
            }
        };

        var session = await _repository.CreateSessionAsync("WS Load Test", new List<InspectionSessionRow> { row });

        var loadedRows = await _repository.LoadRowsAsync(session.Id);

        Assert.Single(loadedRows);
        var loadedRow = loadedRows[0];
        Assert.True(loadedRow.IsWebSocket);
        Assert.True(loadedRow.WebSocketClosed);
        Assert.Equal(2, loadedRow.WebSocketFrames.Count);
        Assert.Equal("Client", loadedRow.WebSocketFrames[0].Direction);
        Assert.Equal("Text", loadedRow.WebSocketFrames[0].FrameType);
        Assert.Equal("hello", loadedRow.WebSocketFrames[0].Payload);
        Assert.Equal("Server", loadedRow.WebSocketFrames[1].Direction);
        Assert.Equal("Binary", loadedRow.WebSocketFrames[1].FrameType);
        Assert.Null(loadedRow.WebSocketFrames[1].Payload);
    }

    [Fact]
    public async Task UpdateSessionAsync_ReplacesWebSocketFrames()
    {
        var row = new InspectionSessionRow
        {
            Method = "GET",
            Url = "wss://example.com/ws",
            Timestamp = DateTime.UtcNow,
            IsWebSocket = true,
            WebSocketClosed = false
        };
        row.WebSocketFrames = new List<InspectionSessionWebSocketFrame>
        {
            new()
            {
                SessionRowId = row.Id,
                Timestamp = DateTime.UtcNow,
                Direction = "Client",
                FrameType = "Text",
                Payload = "old"
            }
        };

        var session = await _repository.CreateSessionAsync("WS Update Test", new List<InspectionSessionRow> { row });

        var newRow = new InspectionSessionRow
        {
            Method = "GET",
            Url = "wss://example.com/ws",
            Timestamp = DateTime.UtcNow,
            IsWebSocket = true,
            WebSocketClosed = true
        };
        newRow.WebSocketFrames = new List<InspectionSessionWebSocketFrame>
        {
            new()
            {
                SessionRowId = newRow.Id,
                Timestamp = DateTime.UtcNow,
                Direction = "Client",
                FrameType = "Text",
                Payload = "new1"
            },
            new()
            {
                SessionRowId = newRow.Id,
                Timestamp = DateTime.UtcNow.AddMilliseconds(50),
                Direction = "Server",
                FrameType = "Text",
                Payload = "new2"
            }
        };

        await _repository.UpdateSessionAsync(session.Id, new List<InspectionSessionRow> { newRow });

        var loadedRows = await _repository.LoadRowsAsync(session.Id);
        Assert.Single(loadedRows);
        Assert.True(loadedRows[0].WebSocketClosed);
        Assert.Equal(2, loadedRows[0].WebSocketFrames.Count);
        Assert.Equal("new1", loadedRows[0].WebSocketFrames[0].Payload);
        Assert.Equal("new2", loadedRows[0].WebSocketFrames[1].Payload);

        // Old frames should be gone
        Assert.Equal(2, await _dbContext.InspectionSessionWebSocketFrames.CountAsync());
    }

    [Fact]
    public async Task DeleteSessionAsync_CascadeDeletesWebSocketFrames()
    {
        var row = new InspectionSessionRow
        {
            Method = "GET",
            Url = "wss://example.com/ws",
            Timestamp = DateTime.UtcNow,
            IsWebSocket = true,
            WebSocketClosed = false
        };
        row.WebSocketFrames = new List<InspectionSessionWebSocketFrame>
        {
            new()
            {
                SessionRowId = row.Id,
                Timestamp = DateTime.UtcNow,
                Direction = "Client",
                FrameType = "Text",
                Payload = "test"
            }
        };

        var session = await _repository.CreateSessionAsync("WS Delete Test", new List<InspectionSessionRow> { row });
        await _repository.DeleteSessionAsync(session.Id);

        Assert.Equal(0, await _dbContext.InspectionSessionWebSocketFrames.CountAsync());
    }

    [Fact]
    public async Task LoadRowsAsync_NonWebSocketRow_HasEmptyFrames()
    {
        var row = new InspectionSessionRow
        {
            Method = "GET",
            Url = "https://example.com/api",
            Timestamp = DateTime.UtcNow,
            IsWebSocket = false,
            WebSocketClosed = false
        };

        var session = await _repository.CreateSessionAsync("HTTP Test", new List<InspectionSessionRow> { row });

        var loadedRows = await _repository.LoadRowsAsync(session.Id);
        Assert.Single(loadedRows);
        Assert.False(loadedRows[0].IsWebSocket);
        Assert.Empty(loadedRows[0].WebSocketFrames);
    }

    [Fact]
    public async Task CreateSessionAsync_MixedRows_PersistsBoth()
    {
        var httpRow = new InspectionSessionRow
        {
            Method = "GET",
            Url = "https://example.com/api",
            Timestamp = DateTime.UtcNow,
            IsWebSocket = false
        };

        var wsRow = new InspectionSessionRow
        {
            Method = "GET",
            Url = "wss://example.com/ws",
            Timestamp = DateTime.UtcNow.AddSeconds(1),
            IsWebSocket = true,
            WebSocketClosed = true
        };
        wsRow.WebSocketFrames = new List<InspectionSessionWebSocketFrame>
        {
            new()
            {
                SessionRowId = wsRow.Id,
                Timestamp = DateTime.UtcNow,
                Direction = "Client",
                FrameType = "Text",
                Payload = "hello"
            }
        };

        var session = await _repository.CreateSessionAsync("Mixed Test",
            new List<InspectionSessionRow> { httpRow, wsRow });

        Assert.Equal(2, session.RowCount);

        var loadedRows = await _repository.LoadRowsAsync(session.Id);
        Assert.Equal(2, loadedRows.Count);

        var loadedHttp = loadedRows.First(r => !r.IsWebSocket);
        var loadedWs = loadedRows.First(r => r.IsWebSocket);

        Assert.Empty(loadedHttp.WebSocketFrames);
        Assert.Single(loadedWs.WebSocketFrames);
        Assert.Equal("hello", loadedWs.WebSocketFrames[0].Payload);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
