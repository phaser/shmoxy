using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using shmoxy.api.Controllers;
using shmoxy.api.ipc;
using shmoxy.api.models;
using shmoxy.api.server;
using shmoxy.shared.ipc;

namespace shmoxy.api.tests.Controllers;

public class InspectionControllerTests
{
    private readonly Mock<IProxyProcessManager> _mockProcessManager;
    private readonly Mock<IRemoteProxyRegistry> _mockRegistry;
    private readonly Mock<ILogger<InspectionController>> _mockLogger;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Mock<IProxyIpcClient> _mockIpcClient;
    private readonly InspectionController _controller;

    public InspectionControllerTests()
    {
        _mockProcessManager = new Mock<IProxyProcessManager>();
        _mockRegistry = new Mock<IRemoteProxyRegistry>();
        _mockLogger = new Mock<ILogger<InspectionController>>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _loggerFactory = NullLoggerFactory.Instance;
        _mockIpcClient = new Mock<IProxyIpcClient>();

        _mockProcessManager.Setup(m => m.GetIpcClient()).Returns(_mockIpcClient.Object);

        _controller = new InspectionController(
            _mockProcessManager.Object,
            _mockRegistry.Object,
            _mockLogger.Object,
            _mockHttpClientFactory.Object,
            _loggerFactory);
    }

    private static DefaultHttpContext CreateHttpContextWithMemoryBody()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        return httpContext;
    }

    [Fact]
    public async Task GetStream_LocalProxy_NotRunning_WritesNoEvents()
    {
        // When the local proxy is not running, GetLocalStream throws InvalidOperationException
        // which is caught by the controller's catch-all and logged -- no SSE events are written
        _mockProcessManager.Setup(m => m.GetStateAsync())
            .ReturnsAsync(new ProxyInstanceState { State = ProxyProcessState.Stopped });

        var httpContext = CreateHttpContextWithMemoryBody();
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        await _controller.GetStream("local", CancellationToken.None);

        // The response body should have no SSE data events written (only headers were set)
        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(httpContext.Response.Body);
        var body = await reader.ReadToEndAsync();
        Assert.DoesNotContain("data:", body);
    }

    [Fact]
    public async Task GetStream_LocalProxy_NullState_WritesNoEvents()
    {
        // GetStateAsync returns null -- proxy has never been started
        _mockProcessManager.Setup(m => m.GetStateAsync())
            .ReturnsAsync((ProxyInstanceState?)null);

        var httpContext = CreateHttpContextWithMemoryBody();
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        await _controller.GetStream("local", CancellationToken.None);

        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(httpContext.Response.Body);
        var body = await reader.ReadToEndAsync();
        Assert.DoesNotContain("data:", body);
    }

    [Fact]
    public async Task GetStream_RemoteProxy_NotFound_WritesNoEvents()
    {
        // Remote proxy ID does not exist in registry
        _mockRegistry.Setup(r => r.GetByIdAsync("nonexistent-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RemoteProxy?)null);

        var httpContext = CreateHttpContextWithMemoryBody();
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        await _controller.GetStream("nonexistent-id", CancellationToken.None);

        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(httpContext.Response.Body);
        var body = await reader.ReadToEndAsync();
        Assert.DoesNotContain("data:", body);
    }

    [Fact]
    public async Task GetStream_SetsSSEContentType()
    {
        // Even when the stream fails, the SSE headers should be set before any streaming begins
        _mockProcessManager.Setup(m => m.GetStateAsync())
            .ReturnsAsync(new ProxyInstanceState { State = ProxyProcessState.Stopped });

        var httpContext = CreateHttpContextWithMemoryBody();
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        await _controller.GetStream("local", CancellationToken.None);

        Assert.Equal("text/event-stream", httpContext.Response.ContentType);
    }

    [Fact]
    public async Task GetStream_SetsCacheControlHeader()
    {
        _mockProcessManager.Setup(m => m.GetStateAsync())
            .ReturnsAsync(new ProxyInstanceState { State = ProxyProcessState.Stopped });

        var httpContext = CreateHttpContextWithMemoryBody();
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        await _controller.GetStream("local", CancellationToken.None);

        Assert.Equal("no-cache", httpContext.Response.Headers["Cache-Control"].ToString());
    }

    [Fact]
    public async Task GetStream_SetsConnectionKeepAliveHeader()
    {
        _mockProcessManager.Setup(m => m.GetStateAsync())
            .ReturnsAsync(new ProxyInstanceState { State = ProxyProcessState.Stopped });

        var httpContext = CreateHttpContextWithMemoryBody();
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        await _controller.GetStream("local", CancellationToken.None);

        Assert.Equal("keep-alive", httpContext.Response.Headers["Connection"].ToString());
    }

    [Fact]
    public async Task GetStream_LocalProxy_Running_StreamsEvents()
    {
        var events = new List<InspectionEvent>
        {
            new() { Timestamp = DateTime.UtcNow, EventType = "request", Method = "GET", Url = "https://example.com" },
            new() { Timestamp = DateTime.UtcNow, EventType = "response", Method = "GET", Url = "https://example.com", StatusCode = 200 }
        };

        _mockProcessManager.Setup(m => m.GetStateAsync())
            .ReturnsAsync(new ProxyInstanceState { State = ProxyProcessState.Running });
        _mockIpcClient.Setup(m => m.EnableInspectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EnableInspectionResponse());
        _mockIpcClient.Setup(m => m.GetInspectionStreamAsync(It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(events));

        var httpContext = CreateHttpContextWithMemoryBody();
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        await _controller.GetStream("local", CancellationToken.None);

        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(httpContext.Response.Body);
        var body = await reader.ReadToEndAsync();

        // Should contain two SSE data lines (one per event)
        var dataLines = body.Split('\n').Where(l => l.StartsWith("data:")).ToList();
        Assert.Equal(2, dataLines.Count);
        Assert.Contains("example.com", body);
    }

    [Fact]
    public async Task GetStream_LocalProxy_Running_EnablesInspection()
    {
        _mockProcessManager.Setup(m => m.GetStateAsync())
            .ReturnsAsync(new ProxyInstanceState { State = ProxyProcessState.Running });
        _mockIpcClient.Setup(m => m.EnableInspectionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EnableInspectionResponse());
        _mockIpcClient.Setup(m => m.GetInspectionStreamAsync(It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(new List<InspectionEvent>()));

        var httpContext = CreateHttpContextWithMemoryBody();
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        await _controller.GetStream("local", CancellationToken.None);

        _mockIpcClient.Verify(m => m.EnableInspectionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetStream_OperationCancelled_HandledGracefully()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _mockProcessManager.Setup(m => m.GetStateAsync())
            .ReturnsAsync(new ProxyInstanceState { State = ProxyProcessState.Running });
        _mockIpcClient.Setup(m => m.EnableInspectionAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var httpContext = CreateHttpContextWithMemoryBody();
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        // Should not throw -- controller catches OperationCanceledException
        await _controller.GetStream("local", cts.Token);

        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(httpContext.Response.Body);
        var body = await reader.ReadToEndAsync();
        Assert.DoesNotContain("data:", body);
    }

    private static async IAsyncEnumerable<InspectionEvent> ToAsyncEnumerable(
        IEnumerable<InspectionEvent> items)
    {
        foreach (var item in items)
        {
            yield return item;
        }

        await Task.CompletedTask;
    }
}
