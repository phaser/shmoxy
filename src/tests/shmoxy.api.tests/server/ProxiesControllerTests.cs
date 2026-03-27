using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using shmoxy.api.Controllers;
using shmoxy.api.models;
using shmoxy.api.server;

namespace shmoxy.api.tests.server;

public class ProxiesControllerTests
{
    private readonly Mock<IProxyProcessManager> _mockProcessManager;
    private readonly Mock<ILogger<ProxiesController>> _mockLogger;
    private readonly ProxiesController _controller;

    public ProxiesControllerTests()
    {
        _mockProcessManager = new Mock<IProxyProcessManager>();
        _mockLogger = new Mock<ILogger<ProxiesController>>();
        _controller = new ProxiesController(_mockLogger.Object, _mockProcessManager.Object);
    }

    [Fact]
    public async Task GetProxyState_ReturnsOkWithState()
    {
        var expectedState = new ProxyInstanceState
        {
            Id = "test-id",
            State = ProxyProcessState.Running,
            ProcessId = 12345,
            SocketPath = "/tmp/test.sock",
            Port = 8080
        };
        _mockProcessManager.Setup(m => m.GetStateAsync())
            .ReturnsAsync(expectedState);

        var result = await _controller.GetProxyState(CancellationToken.None);

        var okResult = Assert.IsType<ActionResult<ProxyInstanceState>>(result);
        var actionResult = Assert.IsType<OkObjectResult>(okResult.Result);
        var state = Assert.IsType<ProxyInstanceState>(actionResult.Value);
        Assert.Equal("test-id", state.Id);
        Assert.Equal(ProxyProcessState.Running, state.State);
    }

    [Fact]
    public async Task GetProxyState_ReturnsNotFound_WhenStateIsNull()
    {
        _mockProcessManager.Setup(m => m.GetStateAsync())
            .ReturnsAsync((ProxyInstanceState?)null);

        var result = await _controller.GetProxyState(CancellationToken.None);

        var actionResult = Assert.IsType<ActionResult<ProxyInstanceState>>(result);
        Assert.IsType<NotFoundObjectResult>(actionResult.Result);
    }

    [Fact]
    public async Task StartProxy_ReturnsOkWithState()
    {
        var expectedState = new ProxyInstanceState
        {
            Id = "test-id",
            State = ProxyProcessState.Running,
            ProcessId = 12345,
            SocketPath = "/tmp/test.sock",
            Port = 8080
        };
        _mockProcessManager.Setup(m => m.StartAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedState);

        var result = await _controller.StartProxy(CancellationToken.None);

        var okResult = Assert.IsType<ActionResult<ProxyInstanceState>>(result);
        var actionResult = Assert.IsType<OkObjectResult>(okResult.Result);
        var state = Assert.IsType<ProxyInstanceState>(actionResult.Value);
        Assert.Equal(ProxyProcessState.Running, state.State);
    }

    [Fact]
    public async Task StartProxy_ReturnsBadRequest_WhenInvalidOperationException()
    {
        _mockProcessManager.Setup(m => m.StartAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Proxy binary not found"));

        var result = await _controller.StartProxy(CancellationToken.None);

        var actionResult = Assert.IsType<ActionResult<ProxyInstanceState>>(result);
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(actionResult.Result);
        Assert.Contains("Message", badRequestResult.Value?.ToString() ?? "");
    }

    [Fact]
    public async Task StartProxy_Returns500_WhenUnexpectedException()
    {
        _mockProcessManager.Setup(m => m.StartAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected error"));

        var result = await _controller.StartProxy(CancellationToken.None);

        var actionResult = Assert.IsType<ActionResult<ProxyInstanceState>>(result);
        var statusCodeResult = Assert.IsType<ObjectResult>(actionResult.Result);
        Assert.Equal(500, statusCodeResult.StatusCode);
    }

    [Fact]
    public async Task StopProxy_ReturnsOk()
    {
        _mockProcessManager.Setup(m => m.StopAsync(It.IsAny<ShutdownSource>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _controller.StopProxy(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("Message", okResult.Value?.ToString() ?? "");
    }

    [Fact]
    public async Task StopProxy_Returns500_WhenException()
    {
        _mockProcessManager.Setup(m => m.StopAsync(It.IsAny<ShutdownSource>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Stop failed"));

        var result = await _controller.StopProxy(CancellationToken.None);

        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, statusCodeResult.StatusCode);
    }

    [Fact]
    public async Task RestartProxy_StopsThenStarts()
    {
        var expectedState = new ProxyInstanceState
        {
            Id = "test-id",
            State = ProxyProcessState.Running,
            ProcessId = 12345,
            SocketPath = "/tmp/test.sock",
            Port = 8080
        };
        _mockProcessManager.Setup(m => m.StopAsync(It.IsAny<ShutdownSource>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockProcessManager.Setup(m => m.StartAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedState);

        var result = await _controller.RestartProxy(CancellationToken.None);

        var okResult = Assert.IsType<ActionResult<ProxyInstanceState>>(result);
        var actionResult = Assert.IsType<OkObjectResult>(okResult.Result);
        var state = Assert.IsType<ProxyInstanceState>(actionResult.Value);
        Assert.Equal(ProxyProcessState.Running, state.State);

        _mockProcessManager.Verify(m => m.StopAsync(It.IsAny<ShutdownSource>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockProcessManager.Verify(m => m.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RestartProxy_Returns500_WhenException()
    {
        _mockProcessManager.Setup(m => m.StopAsync(It.IsAny<ShutdownSource>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Restart failed"));

        var result = await _controller.RestartProxy(CancellationToken.None);

        var actionResult = Assert.IsType<ActionResult<ProxyInstanceState>>(result);
        var statusCodeResult = Assert.IsType<ObjectResult>(actionResult.Result);
        Assert.Equal(500, statusCodeResult.StatusCode);
    }

    [Fact]
    public async Task StopProxy_PassesUserShutdownSource()
    {
        _mockProcessManager.Setup(m => m.StopAsync(It.IsAny<ShutdownSource>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _controller.StopProxy(CancellationToken.None);

        _mockProcessManager.Verify(m => m.StopAsync(ShutdownSource.User, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RestartProxy_PassesUserShutdownSource()
    {
        var expectedState = new ProxyInstanceState
        {
            Id = "test-id",
            State = ProxyProcessState.Running,
            ProcessId = 12345,
            SocketPath = "/tmp/test.sock",
            Port = 8080
        };
        _mockProcessManager.Setup(m => m.StopAsync(It.IsAny<ShutdownSource>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockProcessManager.Setup(m => m.StartAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedState);

        await _controller.RestartProxy(CancellationToken.None);

        _mockProcessManager.Verify(m => m.StopAsync(ShutdownSource.User, It.IsAny<CancellationToken>()), Times.Once);
    }
}
