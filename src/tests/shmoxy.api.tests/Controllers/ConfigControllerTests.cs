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
    public async Task UpdateConfig_AlwaysRestartsProxy()
    {
        var newConfig = new ProxyConfig { Port = 8080, LogLevel = ProxyConfig.LogLevelEnum.Warn };
        var appliedConfig = new ProxyConfig { Port = 8080, LogLevel = ProxyConfig.LogLevelEnum.Warn };

        _mockProcessManager.Setup(m => m.GetStateAsync())
            .ReturnsAsync(new ProxyInstanceState { State = ProxyProcessState.Running });
        _mockProcessManager.Setup(m => m.RestartAsync(8080, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProxyInstanceState { State = ProxyProcessState.Running, Port = 8080 });
        _mockIpcClient.Setup(m => m.GetConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(appliedConfig);

        var result = await _controller.UpdateConfig("local", newConfig, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(okResult.Value);
        _mockProcessManager.Verify(m => m.RestartAsync(8080, It.IsAny<CancellationToken>()), Times.Once);
        _mockConfigPersistence.Verify(m => m.SaveAsync(newConfig, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateConfig_PersistsBeforeRestart()
    {
        var newConfig = new ProxyConfig { Port = 9999, LogLevel = ProxyConfig.LogLevelEnum.Debug };
        var callOrder = new List<string>();

        _mockProcessManager.Setup(m => m.GetStateAsync())
            .ReturnsAsync(new ProxyInstanceState { State = ProxyProcessState.Running });
        _mockConfigPersistence.Setup(m => m.SaveAsync(It.IsAny<ProxyConfig>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("persist"));
        _mockProcessManager.Setup(m => m.RestartAsync(It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("restart"))
            .ReturnsAsync(new ProxyInstanceState { State = ProxyProcessState.Running, Port = 9999 });
        _mockIpcClient.Setup(m => m.GetConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(newConfig);

        await _controller.UpdateConfig("local", newConfig, CancellationToken.None);

        Assert.Equal(new[] { "persist", "restart" }, callOrder);
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

    [Fact]
    public async Task GetConfig_ProxyNotRunning_ReturnsPersistedConfig()
    {
        var persistedConfig = new ProxyConfig { Port = 9999, LogLevel = ProxyConfig.LogLevelEnum.Warn };

        _mockProcessManager.Setup(m => m.GetStateAsync())
            .ReturnsAsync(new ProxyInstanceState { State = ProxyProcessState.Stopped });
        _mockConfigPersistence.Setup(m => m.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(persistedConfig);

        var result = await _controller.GetConfig("local", CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var config = Assert.IsType<ProxyConfig>(okResult.Value);
        Assert.Equal(9999, config.Port);
        Assert.Equal(ProxyConfig.LogLevelEnum.Warn, config.LogLevel);
    }

    [Fact]
    public async Task GetConfig_ProxyNotRunning_NoPersistedConfig_ReturnsDefaults()
    {
        _mockProcessManager.Setup(m => m.GetStateAsync())
            .ReturnsAsync(new ProxyInstanceState { State = ProxyProcessState.Stopped });
        _mockConfigPersistence.Setup(m => m.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProxyConfig?)null);

        var result = await _controller.GetConfig("local", CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var config = Assert.IsType<ProxyConfig>(okResult.Value);
        Assert.Equal(8080, config.Port); // default
    }

    [Fact]
    public async Task UpdateConfig_HttpsPortNegative_Returns400()
    {
        var config = new ProxyConfig { Port = 8080, HttpsPort = -1 };

        _mockProcessManager.Setup(m => m.GetStateAsync())
            .ReturnsAsync(new ProxyInstanceState { State = ProxyProcessState.Running });

        var result = await _controller.UpdateConfig("local", config, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task UpdateConfig_HttpsPortSameAsPort_Returns400()
    {
        var config = new ProxyConfig { Port = 8080, HttpsPort = 8080 };

        _mockProcessManager.Setup(m => m.GetStateAsync())
            .ReturnsAsync(new ProxyInstanceState { State = ProxyProcessState.Running });

        var result = await _controller.UpdateConfig("local", config, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task UpdateConfig_HttpsPortWithoutCerts_Returns400()
    {
        var config = new ProxyConfig { Port = 8080, HttpsPort = 8443 };

        _mockProcessManager.Setup(m => m.GetStateAsync())
            .ReturnsAsync(new ProxyInstanceState { State = ProxyProcessState.Running });

        var result = await _controller.UpdateConfig("local", config, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task UpdateConfig_HttpsPortWithCerts_Succeeds()
    {
        var config = new ProxyConfig
        {
            Port = 8080,
            HttpsPort = 8443,
            CertPath = "/path/to/cert.pem",
            KeyPath = "/path/to/key.pem"
        };
        var appliedConfig = new ProxyConfig { Port = 8080, HttpsPort = 8443 };

        _mockProcessManager.Setup(m => m.GetStateAsync())
            .ReturnsAsync(new ProxyInstanceState { State = ProxyProcessState.Running });
        _mockProcessManager.Setup(m => m.RestartAsync(8080, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProxyInstanceState { State = ProxyProcessState.Running, Port = 8080 });
        _mockIpcClient.Setup(m => m.GetConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(appliedConfig);

        var result = await _controller.UpdateConfig("local", config, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task UpdateConfig_HttpsPortZero_NoCertsRequired()
    {
        var config = new ProxyConfig { Port = 8080, HttpsPort = 0 };
        var appliedConfig = new ProxyConfig { Port = 8080, HttpsPort = 0 };

        _mockProcessManager.Setup(m => m.GetStateAsync())
            .ReturnsAsync(new ProxyInstanceState { State = ProxyProcessState.Running });
        _mockProcessManager.Setup(m => m.RestartAsync(8080, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProxyInstanceState { State = ProxyProcessState.Running, Port = 8080 });
        _mockIpcClient.Setup(m => m.GetConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(appliedConfig);

        var result = await _controller.UpdateConfig("local", config, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
    }
}
