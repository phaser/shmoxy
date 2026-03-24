using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using shmoxy.api.ipc;
using shmoxy.api.models;
using shmoxy.api.models.configuration;
using shmoxy.api.server;

namespace shmoxy.api.tests.server;

public class ProxyProcessManagerTests
{
    private readonly Mock<IProxyIpcClient> _mockIpcClient;
    private readonly Mock<IOptions<ApiConfig>> _mockConfig;
    private readonly Mock<ILogger<ProxyProcessManager>> _mockLogger;
    private readonly ApiConfig _config;

    public ProxyProcessManagerTests()
    {
        _mockIpcClient = new Mock<IProxyIpcClient>();
        _mockConfig = new Mock<IOptions<ApiConfig>>();
        _mockLogger = new Mock<ILogger<ProxyProcessManager>>();
        _config = new ApiConfig
        {
            ProxyPort = 8080,
            ProxyIpcSocketPath = "/tmp/test-shmoxy.sock",
            ProxyBinaryPath = "/bin/sh"  // Use a binary that exists on Unix systems
        };
        _mockConfig.Setup(c => c.Value).Returns(_config);
    }

    [Fact]
    public async Task GetStateAsync_ReturnsInitialState()
    {
        var manager = new ProxyProcessManager(_mockLogger.Object, _mockIpcClient.Object, _mockConfig.Object);

        var state = await manager.GetStateAsync();

        Assert.NotNull(state);
        Assert.Equal(ProxyProcessState.Stopped, state.State);
        Assert.Equal(_config.ProxyIpcSocketPath, state.SocketPath);
    }

    [Fact]
    public async Task IsRunningAsync_ReturnsFalse_WhenStopped()
    {
        var manager = new ProxyProcessManager(_mockLogger.Object, _mockIpcClient.Object, _mockConfig.Object);

        var running = await manager.IsRunningAsync();

        Assert.False(running);
    }

    [Fact]
    public async Task StartAsync_UpdatesStateToStarting()
    {
        var manager = new ProxyProcessManager(_mockLogger.Object, _mockIpcClient.Object, _mockConfig.Object);
        _mockIpcClient.Setup(c => c.IsHealthyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var state = await manager.StartAsync();

        Assert.Equal(ProxyProcessState.Running, state.State);
        Assert.Equal(_config.ProxyPort, state.Port);
        Assert.Equal(_config.ProxyIpcSocketPath, state.SocketPath);
    }

    [Fact]
    public async Task StartAsync_Throws_WhenBinaryNotFound()
    {
        var config = new ApiConfig
        {
            ProxyPort = 8080,
            ProxyIpcSocketPath = "/tmp/test-shmoxy.sock",
            ProxyBinaryPath = "/nonexistent/path/to/shmoxy"
        };
        var mockConfig = new Mock<IOptions<ApiConfig>>();
        mockConfig.Setup(c => c.Value).Returns(config);

        var manager = new ProxyProcessManager(_mockLogger.Object, _mockIpcClient.Object, mockConfig.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.StartAsync());
    }

    [Fact]
    public async Task StartAsync_UpdatesStateToCrashed_WhenHealthCheckFails()
    {
        var manager = new ProxyProcessManager(_mockLogger.Object, _mockIpcClient.Object, _mockConfig.Object);
        _mockIpcClient.Setup(c => c.IsHealthyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await Assert.ThrowsAsync<TimeoutException>(() => manager.StartAsync());

        var state = await manager.GetStateAsync();
        Assert.NotNull(state);
        Assert.Equal(ProxyProcessState.Crashed, state.State);
    }

    [Fact]
    public async Task StartAsync_WhenAlreadyRunning_ReturnsCurrentState()
    {
        var manager = new ProxyProcessManager(_mockLogger.Object, _mockIpcClient.Object, _mockConfig.Object);
        _mockIpcClient.Setup(c => c.IsHealthyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await manager.StartAsync();
        var state = await manager.StartAsync();

        Assert.Equal(ProxyProcessState.Running, state.State);
    }

    [Fact]
    public async Task StopAsync_WhenStopped_DoesNothing()
    {
        var manager = new ProxyProcessManager(_mockLogger.Object, _mockIpcClient.Object, _mockConfig.Object);

        await manager.StopAsync();

        var state = await manager.GetStateAsync();
        Assert.NotNull(state);
        Assert.Equal(ProxyProcessState.Stopped, state.State);
    }

    [Fact]
    public async Task StopAsync_WhenRunning_StopsProcess()
    {
        var manager = new ProxyProcessManager(_mockLogger.Object, _mockIpcClient.Object, _mockConfig.Object);
        _mockIpcClient.Setup(c => c.IsHealthyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await manager.StartAsync();
        await manager.StopAsync();

        var state = await manager.GetStateAsync();
        Assert.NotNull(state);
        Assert.Equal(ProxyProcessState.Stopped, state.State);
    }

    [Fact]
    public async Task StopAsync_CallsShutdownViaIpc()
    {
        var manager = new ProxyProcessManager(_mockLogger.Object, _mockIpcClient.Object, _mockConfig.Object);
        _mockIpcClient.Setup(c => c.IsHealthyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await manager.StartAsync();
        await manager.StopAsync();

        _mockIpcClient.Verify(c => c.ShutdownAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_RaisesStateChangedEvent()
    {
        var manager = new ProxyProcessManager(_mockLogger.Object, _mockIpcClient.Object, _mockConfig.Object);
        _mockIpcClient.Setup(c => c.IsHealthyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var events = new List<ProxyInstanceState>();
        manager.OnStateChanged += (s, e) => events.Add(e);

        await manager.StartAsync();

        Assert.NotEmpty(events);
        Assert.Contains(events, e => e.State == ProxyProcessState.Starting);
        Assert.Contains(events, e => e.State == ProxyProcessState.Running);
    }

    [Fact]
    public async Task Dispose_DoesNotThrow()
    {
        var manager = new ProxyProcessManager(_mockLogger.Object, _mockIpcClient.Object, _mockConfig.Object);

        manager.Dispose();

        var state = await manager.GetStateAsync();
        Assert.NotNull(state);
        Assert.Equal(ProxyProcessState.Stopped, state.State);
    }

    [Fact]
    public async Task Dispose_AfterStart_CallsShutdown()
    {
        var manager = new ProxyProcessManager(_mockLogger.Object, _mockIpcClient.Object, _mockConfig.Object);
        _mockIpcClient.Setup(c => c.IsHealthyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await manager.StartAsync();
        manager.Dispose();

        _mockIpcClient.Verify(c => c.ShutdownAsync(It.IsAny<CancellationToken>()), Times.Once);

        var state = await manager.GetStateAsync();
        Assert.NotNull(state);
        Assert.Equal(ProxyProcessState.Stopped, state.State);
    }

    [Fact]
    public async Task StopAsync_WhenShutdownFails_DoesNotThrow()
    {
        var manager = new ProxyProcessManager(_mockLogger.Object, _mockIpcClient.Object, _mockConfig.Object);
        _mockIpcClient.Setup(c => c.IsHealthyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockIpcClient.Setup(c => c.ShutdownAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        await manager.StartAsync();

        var exception = await Record.ExceptionAsync(() => manager.StopAsync());
        Assert.Null(exception);

        var state = await manager.GetStateAsync();
        Assert.NotNull(state);
        Assert.Equal(ProxyProcessState.Stopped, state.State);
    }
}
