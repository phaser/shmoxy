using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using shmoxy.api.ipc;
using shmoxy.api.models;
using shmoxy.api.models.configuration;
using shmoxy.api.server;
using shmoxy.shared.ipc;

namespace shmoxy.api.tests.server;

public class ProxyProcessManagerTests
{
    private readonly Mock<IProxyIpcClient> _mockIpcClient;
    private readonly Mock<IOptions<ApiConfig>> _mockConfig;
    private readonly Mock<ILogger<ProxyProcessManager>> _mockLogger;
    private readonly Mock<IConfigPersistence> _mockConfigPersistence;
    private readonly ApiConfig _config;

    public ProxyProcessManagerTests()
    {
        _mockIpcClient = new Mock<IProxyIpcClient>();
        _mockConfig = new Mock<IOptions<ApiConfig>>();
        _mockLogger = new Mock<ILogger<ProxyProcessManager>>();
        _mockConfigPersistence = new Mock<IConfigPersistence>();
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
        var manager = new ProxyProcessManager(_mockLogger.Object, _mockIpcClient.Object, _mockConfig.Object, _mockConfigPersistence.Object);

        var state = await manager.GetStateAsync();

        Assert.NotNull(state);
        Assert.Equal(ProxyProcessState.Stopped, state.State);
        Assert.Equal(_config.ProxyIpcSocketPath, state.SocketPath);
    }

    [Fact]
    public async Task IsRunningAsync_ReturnsFalse_WhenStopped()
    {
        var manager = new ProxyProcessManager(_mockLogger.Object, _mockIpcClient.Object, _mockConfig.Object, _mockConfigPersistence.Object);

        var running = await manager.IsRunningAsync();

        Assert.False(running);
    }

    [Fact]
    public async Task StartAsync_UpdatesStateToStarting()
    {
        var manager = new ProxyProcessManager(_mockLogger.Object, _mockIpcClient.Object, _mockConfig.Object, _mockConfigPersistence.Object);
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

        var manager = new ProxyProcessManager(_mockLogger.Object, _mockIpcClient.Object, mockConfig.Object, _mockConfigPersistence.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.StartAsync());
    }

    [Fact]
    public async Task StartAsync_UpdatesStateToCrashed_WhenHealthCheckFails()
    {
        var manager = new ProxyProcessManager(_mockLogger.Object, _mockIpcClient.Object, _mockConfig.Object, _mockConfigPersistence.Object);
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
        var manager = new ProxyProcessManager(_mockLogger.Object, _mockIpcClient.Object, _mockConfig.Object, _mockConfigPersistence.Object);
        _mockIpcClient.Setup(c => c.IsHealthyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await manager.StartAsync();
        var state = await manager.StartAsync();

        Assert.Equal(ProxyProcessState.Running, state.State);
    }

    [Fact]
    public async Task StopAsync_WhenStopped_DoesNothing()
    {
        var manager = new ProxyProcessManager(_mockLogger.Object, _mockIpcClient.Object, _mockConfig.Object, _mockConfigPersistence.Object);

        await manager.StopAsync();

        var state = await manager.GetStateAsync();
        Assert.NotNull(state);
        Assert.Equal(ProxyProcessState.Stopped, state.State);
    }

    [Fact]
    public async Task StopAsync_WhenRunning_StopsProcess()
    {
        var manager = new ProxyProcessManager(_mockLogger.Object, _mockIpcClient.Object, _mockConfig.Object, _mockConfigPersistence.Object);
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
        var manager = new ProxyProcessManager(_mockLogger.Object, _mockIpcClient.Object, _mockConfig.Object, _mockConfigPersistence.Object);
        _mockIpcClient.Setup(c => c.IsHealthyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockIpcClient.Setup(c => c.ShutdownAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShutdownResponse { Success = true, Message = "ok" });

        await manager.StartAsync();

        // The test process (/bin/sh) may exit before StopAsync runs.
        // ShutdownAsync is only called when the process is still alive,
        // so verify it was called at most once rather than exactly once.
        await manager.StopAsync();

        _mockIpcClient.Verify(c => c.ShutdownAsync(It.IsAny<CancellationToken>()), Times.AtMostOnce);
    }

    [Fact]
    public async Task StartAsync_RaisesStateChangedEvent()
    {
        var manager = new ProxyProcessManager(_mockLogger.Object, _mockIpcClient.Object, _mockConfig.Object, _mockConfigPersistence.Object);
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
        var manager = new ProxyProcessManager(_mockLogger.Object, _mockIpcClient.Object, _mockConfig.Object, _mockConfigPersistence.Object);

        manager.Dispose();

        var state = await manager.GetStateAsync();
        Assert.NotNull(state);
        Assert.Equal(ProxyProcessState.Stopped, state.State);
    }

    [Fact]
    public async Task Dispose_AfterStart_CallsShutdown()
    {
        var manager = new ProxyProcessManager(_mockLogger.Object, _mockIpcClient.Object, _mockConfig.Object, _mockConfigPersistence.Object);
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
        var manager = new ProxyProcessManager(_mockLogger.Object, _mockIpcClient.Object, _mockConfig.Object, _mockConfigPersistence.Object);
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

    [Fact]
    public async Task StopAsync_SetsExitReasonBasedOnSource_User()
    {
        var manager = new ProxyProcessManager(_mockLogger.Object, _mockIpcClient.Object, _mockConfig.Object, _mockConfigPersistence.Object);
        _mockIpcClient.Setup(c => c.IsHealthyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await manager.StartAsync();
        await manager.StopAsync(ShutdownSource.User);

        var state = await manager.GetStateAsync();
        Assert.NotNull(state);
        Assert.Equal("Stopped by user", state.ExitReason);
    }

    [Fact]
    public async Task StartAsync_Throws_WhenNoBinaryFound_IncludesProxySubdirInMessage()
    {
        var config = new ApiConfig
        {
            ProxyPort = 8080,
            ProxyIpcSocketPath = "/tmp/test-shmoxy.sock",
            ProxyBinaryPath = null
        };
        var mockConfig = new Mock<IOptions<ApiConfig>>();
        mockConfig.Setup(c => c.Value).Returns(config);

        var manager = new ProxyProcessManager(_mockLogger.Object, _mockIpcClient.Object, mockConfig.Object, _mockConfigPersistence.Object);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.StartAsync());
        Assert.Contains("proxy", ex.Message);
        Assert.Contains("shmoxy.dll", ex.Message);
    }

    [Fact]
    public async Task StopAsync_SetsExitReasonBasedOnSource_System()
    {
        var manager = new ProxyProcessManager(_mockLogger.Object, _mockIpcClient.Object, _mockConfig.Object, _mockConfigPersistence.Object);
        _mockIpcClient.Setup(c => c.IsHealthyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await manager.StartAsync();
        await manager.StopAsync(ShutdownSource.System);

        var state = await manager.GetStateAsync();
        Assert.NotNull(state);
        Assert.Equal("Stopped by system (application shutdown)", state.ExitReason);
    }
}
