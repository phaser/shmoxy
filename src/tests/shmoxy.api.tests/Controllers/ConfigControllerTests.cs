using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using shmoxy.api.Controllers;
using shmoxy.api.ipc;
using shmoxy.api.models;
using shmoxy.api.server;
using shmoxy.shared.ipc;

namespace shmoxy.api.tests.Controllers;

public class ConfigControllerTests
{
    private readonly Mock<IProxyProcessManager> _mockProcessManager;
    private readonly Mock<IRemoteProxyRegistry> _mockRegistry;
    private readonly Mock<IConfigPersistence> _mockConfigPersistence;
    private readonly Mock<ILogger<ConfigController>> _mockLogger;
    private readonly Mock<IProxyIpcClient> _mockIpcClient;
    private readonly ConfigController _controller;

    public ConfigControllerTests()
    {
        _mockProcessManager = new Mock<IProxyProcessManager>();
        _mockRegistry = new Mock<IRemoteProxyRegistry>();
        _mockConfigPersistence = new Mock<IConfigPersistence>();
        _mockLogger = new Mock<ILogger<ConfigController>>();
        _mockIpcClient = new Mock<IProxyIpcClient>();
        _controller = new ConfigController(_mockProcessManager.Object, _mockRegistry.Object, _mockConfigPersistence.Object, _mockLogger.Object);

        _mockProcessManager.Setup(m => m.GetIpcClient()).Returns(_mockIpcClient.Object);
    }

    [Fact]
    public async Task UpdateConfig_SamePort_DoesNotRestart()
    {
        var currentConfig = new ProxyConfig { Port = 8080, LogLevel = ProxyConfig.LogLevelEnum.Info };
        var newConfig = new ProxyConfig { Port = 8080, LogLevel = ProxyConfig.LogLevelEnum.Warn };

        _mockProcessManager.Setup(m => m.GetStateAsync())
            .ReturnsAsync(new ProxyInstanceState { State = ProxyProcessState.Running });
        _mockIpcClient.Setup(m => m.GetConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentConfig);
        _mockIpcClient.Setup(m => m.UpdateConfigAsync(It.IsAny<ProxyConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(newConfig);

        var result = await _controller.UpdateConfig("local", newConfig, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(okResult.Value);
        _mockProcessManager.Verify(m => m.RestartAsync(It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateConfig_PortChanged_RestartsProxy()
    {
        var currentConfig = new ProxyConfig { Port = 8080, LogLevel = ProxyConfig.LogLevelEnum.Info };
        var newConfig = new ProxyConfig { Port = 9999, LogLevel = ProxyConfig.LogLevelEnum.Info };
        var restartedConfig = new ProxyConfig { Port = 9999, LogLevel = ProxyConfig.LogLevelEnum.Info };

        _mockProcessManager.Setup(m => m.GetStateAsync())
            .ReturnsAsync(new ProxyInstanceState { State = ProxyProcessState.Running });
        _mockIpcClient.Setup(m => m.GetConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentConfig);
        _mockIpcClient.SetupSequence(m => m.UpdateConfigAsync(It.IsAny<ProxyConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(newConfig)     // First call: before restart
            .ReturnsAsync(restartedConfig); // Second call: re-apply after restart
        _mockProcessManager.Setup(m => m.RestartAsync(9999, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProxyInstanceState { State = ProxyProcessState.Running, Port = 9999 });

        var result = await _controller.UpdateConfig("local", newConfig, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        _mockProcessManager.Verify(m => m.RestartAsync(9999, It.IsAny<CancellationToken>()), Times.Once);
        // Config should be re-applied after restart
        _mockIpcClient.Verify(m => m.UpdateConfigAsync(It.IsAny<ProxyConfig>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task UpdateConfig_PersistsConfigToDisk()
    {
        var currentConfig = new ProxyConfig { Port = 8080, LogLevel = ProxyConfig.LogLevelEnum.Info };
        var newConfig = new ProxyConfig { Port = 8080, LogLevel = ProxyConfig.LogLevelEnum.Warn };

        _mockProcessManager.Setup(m => m.GetStateAsync())
            .ReturnsAsync(new ProxyInstanceState { State = ProxyProcessState.Running });
        _mockIpcClient.Setup(m => m.GetConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentConfig);
        _mockIpcClient.Setup(m => m.UpdateConfigAsync(It.IsAny<ProxyConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(newConfig);

        await _controller.UpdateConfig("local", newConfig, CancellationToken.None);

        _mockConfigPersistence.Verify(m => m.SaveAsync(It.IsAny<ProxyConfig>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateConfig_ProxyNotRunning_Returns400()
    {
        var config = new ProxyConfig { Port = 8080, LogLevel = ProxyConfig.LogLevelEnum.Info };

        _mockProcessManager.Setup(m => m.GetStateAsync())
            .ReturnsAsync(new ProxyInstanceState { State = ProxyProcessState.Stopped });

        var result = await _controller.UpdateConfig("local", config, CancellationToken.None);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.NotNull(badRequestResult.Value);
    }
}
