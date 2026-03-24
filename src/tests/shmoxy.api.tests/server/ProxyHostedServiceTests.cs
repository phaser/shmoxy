using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using shmoxy.api.models;
using shmoxy.api.models.configuration;
using shmoxy.api.server;

namespace shmoxy.api.tests.server;

public class ProxyHostedServiceTests
{
    private readonly Mock<ILogger<ProxyHostedService>> _mockLogger;
    private readonly Mock<IProxyProcessManager> _mockProcessManager;
    private readonly Mock<IOptions<ApiConfig>> _mockConfig;

    public ProxyHostedServiceTests()
    {
        _mockLogger = new Mock<ILogger<ProxyHostedService>>();
        _mockProcessManager = new Mock<IProxyProcessManager>();
        _mockConfig = new Mock<IOptions<ApiConfig>>();
    }

    [Fact]
    public async Task StartAsync_AutoStartsProxy_WhenConfigured()
    {
        _mockConfig.Setup(c => c.Value).Returns(new ApiConfig { AutoStartProxy = true });
        _mockProcessManager.Setup(m => m.StartAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProxyInstanceState { State = ProxyProcessState.Running });

        var service = new ProxyHostedService(_mockLogger.Object, _mockProcessManager.Object, _mockConfig.Object);
        await service.StartAsync(CancellationToken.None);

        _mockProcessManager.Verify(m => m.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_DoesNotStartProxy_WhenNotConfigured()
    {
        _mockConfig.Setup(c => c.Value).Returns(new ApiConfig { AutoStartProxy = false });

        var service = new ProxyHostedService(_mockLogger.Object, _mockProcessManager.Object, _mockConfig.Object);
        await service.StartAsync(CancellationToken.None);

        _mockProcessManager.Verify(m => m.StartAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StopAsync_StopsProxy()
    {
        _mockConfig.Setup(c => c.Value).Returns(new ApiConfig { AutoStartProxy = false });

        var service = new ProxyHostedService(_mockLogger.Object, _mockProcessManager.Object, _mockConfig.Object);
        await service.StopAsync(CancellationToken.None);

        _mockProcessManager.Verify(m => m.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StopAsync_DoesNotThrow_WhenStopFails()
    {
        _mockConfig.Setup(c => c.Value).Returns(new ApiConfig { AutoStartProxy = false });
        _mockProcessManager.Setup(m => m.StopAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("test error"));

        var service = new ProxyHostedService(_mockLogger.Object, _mockProcessManager.Object, _mockConfig.Object);

        var exception = await Record.ExceptionAsync(() => service.StopAsync(CancellationToken.None));
        Assert.Null(exception);
    }
}
