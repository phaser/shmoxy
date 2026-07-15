using Microsoft.AspNetCore.Mvc;
using Moq;
using shmoxy.api.Controllers;
using shmoxy.api.models;
using shmoxy.api.models.dto;
using shmoxy.api.server;

namespace shmoxy.api.tests.Controllers;

public class SavedTracesControllerTests
{
    private readonly Mock<ISavedTraceRepository> _mockRepo;
    private readonly SavedTracesController _controller;

    public SavedTracesControllerTests()
    {
        _mockRepo = new Mock<ISavedTraceRepository>();
        _controller = new SavedTracesController(_mockRepo.Object);
    }

    [Fact]
    public async Task SaveTrace_ReturnsBadRequest_WhenMethodEmpty()
    {
        var request = new SavedTraceDto { Method = "", Url = "https://example.com" };

        var result = await _controller.SaveTrace(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        _mockRepo.Verify(r => r.SaveAsync(It.IsAny<SavedTrace>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SaveTrace_ReturnsBadRequest_WhenUrlEmpty()
    {
        var request = new SavedTraceDto { Method = "GET", Url = " " };

        var result = await _controller.SaveTrace(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task SaveTrace_ReturnsCreatedSummary_WithValidRequest()
    {
        _mockRepo.Setup(r => r.SaveAsync(It.IsAny<SavedTrace>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SavedTrace t, CancellationToken _) => t);

        var request = new SavedTraceDto
        {
            Method = "GET",
            Url = "https://example.com/api",
            StatusCode = 200,
            Timestamp = DateTime.UtcNow,
            ResponseBody = "{\"ok\":true}"
        };

        var result = await _controller.SaveTrace(request, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var summary = Assert.IsType<SavedTraceSummaryDto>(created.Value);
        Assert.NotEmpty(summary.Id);
        Assert.Equal("GET", summary.Method);
        Assert.Equal("https://example.com/api", summary.Url);
        Assert.Equal(request.ResponseBody.Length, summary.ResponseBodySize);
    }

    [Fact]
    public async Task SaveTrace_SerializesHeadersAndFrames()
    {
        SavedTrace? captured = null;
        _mockRepo.Setup(r => r.SaveAsync(It.IsAny<SavedTrace>(), It.IsAny<CancellationToken>()))
            .Callback<SavedTrace, CancellationToken>((t, _) => captured = t)
            .ReturnsAsync((SavedTrace t, CancellationToken _) => t);

        var request = new SavedTraceDto
        {
            Method = "GET",
            Url = "wss://example.com/socket",
            IsWebSocket = true,
            RequestHeaders = [new("Host", "example.com"), new("Upgrade", "websocket")],
            WebSocketFrames =
            [
                new WebSocketFrameDto { Direction = "client", FrameType = "text", Payload = "hello", Timestamp = DateTime.UtcNow }
            ]
        };

        await _controller.SaveTrace(request, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Contains("example.com", captured.RequestHeaders);
        Assert.Single(captured.WebSocketFrames);
        Assert.Equal("hello", captured.WebSocketFrames[0].Payload);
        Assert.Equal(captured.Id, captured.WebSocketFrames[0].SavedTraceId);
    }

    [Fact]
    public async Task ListTraces_ReturnsOk()
    {
        _mockRepo.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new SavedTraceSummaryDto { Id = "1", Method = "GET", Url = "https://a" },
                new SavedTraceSummaryDto { Id = "2", Method = "POST", Url = "https://b" }
            ]);

        var result = await _controller.ListTraces(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var traces = Assert.IsType<List<SavedTraceSummaryDto>>(ok.Value);
        Assert.Equal(2, traces.Count);
    }

    [Fact]
    public async Task GetTrace_ReturnsNotFound_WhenMissing()
    {
        _mockRepo.Setup(r => r.GetAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SavedTrace?)null);

        var result = await _controller.GetTrace("missing", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetTrace_RoundTripsHeadersAndFrames()
    {
        var trace = new SavedTrace
        {
            Id = "abc",
            Method = "GET",
            Url = "https://example.com",
            RequestHeaders = "[{\"Key\":\"Host\",\"Value\":\"example.com\"}]",
            WebSocketFrames =
            [
                new SavedTraceWebSocketFrame { Direction = "server", FrameType = "text", Payload = "pong", Timestamp = DateTime.UtcNow }
            ]
        };
        _mockRepo.Setup(r => r.GetAsync("abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(trace);

        var result = await _controller.GetTrace("abc", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<SavedTraceDto>(ok.Value);
        Assert.NotNull(dto.RequestHeaders);
        Assert.Equal("Host", dto.RequestHeaders[0].Key);
        Assert.Equal("example.com", dto.RequestHeaders[0].Value);
        Assert.NotNull(dto.WebSocketFrames);
        Assert.Equal("pong", dto.WebSocketFrames[0].Payload);
    }

    [Fact]
    public async Task UpdateNote_ReturnsNotFound_WhenMissing()
    {
        _mockRepo.Setup(r => r.UpdateNoteAsync("missing", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _controller.UpdateNote("missing", new UpdateSavedTraceNoteRequest { Note = "x" }, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task UpdateNote_ReturnsNoContent_WhenUpdated()
    {
        _mockRepo.Setup(r => r.UpdateNoteAsync("abc", "interesting", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _controller.UpdateNote("abc", new UpdateSavedTraceNoteRequest { Note = "interesting" }, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task DeleteTrace_ReturnsNotFound_WhenMissing()
    {
        _mockRepo.Setup(r => r.DeleteAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _controller.DeleteTrace("missing", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task DeleteTrace_ReturnsNoContent_WhenDeleted()
    {
        _mockRepo.Setup(r => r.DeleteAsync("abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _controller.DeleteTrace("abc", CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }
}
