using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using shmoxy.api.Controllers;
using shmoxy.api.models;
using shmoxy.api.models.dto;
using shmoxy.api.server;

namespace shmoxy.api.tests.Controllers;

public class ResendControllerTests
{
    private readonly Mock<IProxyProcessManager> _mockProcessManager;
    private readonly Mock<ILogger<ResendController>> _mockLogger;
    private readonly ResendController _controller;

    public ResendControllerTests()
    {
        _mockProcessManager = new Mock<IProxyProcessManager>();
        _mockLogger = new Mock<ILogger<ResendController>>();
        _controller = new ResendController(_mockProcessManager.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task Resend_WhenProxyNotRunning_ReturnsBadRequest()
    {
        _mockProcessManager.Setup(m => m.GetStateAsync())
            .ReturnsAsync(new ProxyInstanceState { State = ProxyProcessState.Stopped });

        var request = new ResendRequestDto
        {
            Method = "GET",
            Url = "https://example.com",
            Headers = new List<KeyValuePair<string, string>>()
        };

        var result = await _controller.Resend(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Resend_WhenProxyRunningButNoPort_ReturnsBadRequest()
    {
        _mockProcessManager.Setup(m => m.GetStateAsync())
            .ReturnsAsync(new ProxyInstanceState { State = ProxyProcessState.Running, Port = null });

        var request = new ResendRequestDto
        {
            Method = "GET",
            Url = "https://example.com",
            Headers = new List<KeyValuePair<string, string>>()
        };

        var result = await _controller.Resend(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Resend_WhenUpstreamUnreachable_Returns502()
    {
        // Proxy is "running" on a port that nothing listens on — the resend will fail
        _mockProcessManager.Setup(m => m.GetStateAsync())
            .ReturnsAsync(new ProxyInstanceState
            {
                State = ProxyProcessState.Running,
                Port = 19999 // unused port
            });

        var request = new ResendRequestDto
        {
            Method = "GET",
            Url = "http://192.0.2.1/test", // TEST-NET address, guaranteed unreachable
            Headers = new List<KeyValuePair<string, string>>()
        };

        var result = await _controller.Resend(request, CancellationToken.None);

        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(502, statusResult.StatusCode);
    }
}
