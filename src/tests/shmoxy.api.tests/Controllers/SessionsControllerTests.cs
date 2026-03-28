using Microsoft.AspNetCore.Mvc;
using Moq;
using shmoxy.api.Controllers;
using shmoxy.api.models;
using shmoxy.api.models.dto;
using shmoxy.api.server;

namespace shmoxy.api.tests.Controllers;

public class SessionsControllerTests
{
    private readonly Mock<ISessionRepository> _mockRepo;
    private readonly SessionsController _controller;

    public SessionsControllerTests()
    {
        _mockRepo = new Mock<ISessionRepository>();
        _controller = new SessionsController(_mockRepo.Object);
    }

    [Fact]
    public async Task CreateSession_ReturnsBadRequest_WhenNameEmpty()
    {
        var request = new CreateSessionRequest { Name = "", Rows = [] };

        var result = await _controller.CreateSession(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task CreateSession_ReturnsCreated_WithValidRequest()
    {
        var session = new InspectionSession { Id = "abc", Name = "Test", RowCount = 1 };
        _mockRepo.Setup(r => r.CreateSessionAsync(
            It.IsAny<string>(), It.IsAny<List<InspectionSessionRow>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var request = new CreateSessionRequest
        {
            Name = "Test",
            Rows = [new SessionRowDto { Method = "GET", Url = "https://example.com", Timestamp = DateTime.UtcNow }]
        };

        var result = await _controller.CreateSession(request, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var response = Assert.IsType<SessionResponse>(created.Value);
        Assert.Equal("Test", response.Name);
    }

    [Fact]
    public async Task ListSessions_ReturnsOk()
    {
        _mockRepo.Setup(r => r.ListSessionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new InspectionSession { Name = "Session 1" },
                new InspectionSession { Name = "Session 2" }
            ]);

        var result = await _controller.ListSessions(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var sessions = Assert.IsType<List<SessionResponse>>(ok.Value);
        Assert.Equal(2, sessions.Count);
    }

    [Fact]
    public async Task GetSession_ReturnsNotFound_WhenMissing()
    {
        _mockRepo.Setup(r => r.GetSessionAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((InspectionSession?)null);

        var result = await _controller.GetSession("missing", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetSession_ReturnsRows()
    {
        _mockRepo.Setup(r => r.GetSessionAsync("abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InspectionSession { Id = "abc", Name = "Test" });
        _mockRepo.Setup(r => r.LoadRowsAsync("abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new InspectionSessionRow { Method = "GET", Url = "https://example.com", Timestamp = DateTime.UtcNow }
            ]);

        var result = await _controller.GetSession("abc", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var rows = Assert.IsType<List<SessionRowDto>>(ok.Value);
        Assert.Single(rows);
        Assert.Equal("GET", rows[0].Method);
    }

    [Fact]
    public async Task UpdateSession_ReturnsNotFound_WhenMissing()
    {
        _mockRepo.Setup(r => r.GetSessionAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((InspectionSession?)null);

        var request = new UpdateSessionRequest { Rows = [] };
        var result = await _controller.UpdateSession("missing", request, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task UpdateSession_ReturnsOk_WhenValid()
    {
        var session = new InspectionSession { Id = "abc", Name = "Test", RowCount = 2 };
        _mockRepo.Setup(r => r.GetSessionAsync("abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var request = new UpdateSessionRequest
        {
            Rows = [new SessionRowDto { Method = "POST", Url = "https://example.com", Timestamp = DateTime.UtcNow }]
        };

        var result = await _controller.UpdateSession("abc", request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.IsType<SessionResponse>(ok.Value);
    }

    [Fact]
    public async Task DeleteSession_ReturnsNotFound_WhenMissing()
    {
        _mockRepo.Setup(r => r.GetSessionAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((InspectionSession?)null);

        var result = await _controller.DeleteSession("missing", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task DeleteSession_ReturnsNoContent()
    {
        _mockRepo.Setup(r => r.GetSessionAsync("abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InspectionSession { Id = "abc", Name = "Test" });

        var result = await _controller.DeleteSession("abc", CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        _mockRepo.Verify(r => r.DeleteSessionAsync("abc", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateSession_TrimsName()
    {
        var session = new InspectionSession { Id = "abc", Name = "Trimmed", RowCount = 0 };
        _mockRepo.Setup(r => r.CreateSessionAsync(
            "Trimmed", It.IsAny<List<InspectionSessionRow>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var request = new CreateSessionRequest { Name = "  Trimmed  ", Rows = [] };
        var result = await _controller.CreateSession(request, CancellationToken.None);

        _mockRepo.Verify(r => r.CreateSessionAsync(
            "Trimmed", It.IsAny<List<InspectionSessionRow>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetSession_DeserializesHeadersFromJson()
    {
        _mockRepo.Setup(r => r.GetSessionAsync("abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InspectionSession { Id = "abc", Name = "Test" });
        _mockRepo.Setup(r => r.LoadRowsAsync("abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new InspectionSessionRow
                {
                    Method = "GET",
                    Url = "https://example.com",
                    Timestamp = DateTime.UtcNow,
                    RequestHeaders = """{"Accept":"application/json"}""",
                    ResponseHeaders = """{"Content-Type":"application/json"}"""
                }
            ]);

        var result = await _controller.GetSession("abc", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var rows = Assert.IsType<List<SessionRowDto>>(ok.Value);
        Assert.NotNull(rows[0].RequestHeaders);
        Assert.Equal("application/json", rows[0].RequestHeaders!["Accept"]);
        Assert.NotNull(rows[0].ResponseHeaders);
        Assert.Equal("application/json", rows[0].ResponseHeaders!["Content-Type"]);
    }
}
