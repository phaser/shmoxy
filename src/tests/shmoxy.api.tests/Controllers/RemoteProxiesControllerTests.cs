using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using shmoxy.api.Controllers;
using shmoxy.api.models;
using shmoxy.api.models.dto;
using shmoxy.api.server;

namespace shmoxy.api.tests.Controllers;

public class RemoteProxiesControllerTests
{
    private readonly Mock<IRemoteProxyRegistry> _mockRegistry;
    private readonly Mock<ILogger<RemoteProxiesController>> _mockLogger;
    private readonly RemoteProxiesController _controller;

    public RemoteProxiesControllerTests()
    {
        _mockRegistry = new Mock<IRemoteProxyRegistry>();
        _mockLogger = new Mock<ILogger<RemoteProxiesController>>();
        _controller = new RemoteProxiesController(_mockLogger.Object, _mockRegistry.Object);
    }

    [Fact]
    public async Task GetAllProxies_ReturnsOkWithProxies()
    {
        var proxies = new List<RemoteProxy>
        {
            new() { Id = "1", Name = "Proxy1", AdminUrl = "http://localhost:8081" },
            new() { Id = "2", Name = "Proxy2", AdminUrl = "http://localhost:8082" }
        };
        _mockRegistry.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(proxies);

        var result = await _controller.GetAllProxies(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var responses = Assert.IsAssignableFrom<List<RemoteProxyResponse>>(okResult.Value);
        Assert.Equal(2, responses.Count);
        Assert.Equal("Proxy1", responses[0].Name);
    }

    [Fact]
    public async Task GetProxy_ReturnsOk_WhenFound()
    {
        var proxy = new RemoteProxy { Id = "abc", Name = "TestProxy", AdminUrl = "http://localhost:8081" };
        _mockRegistry.Setup(r => r.GetByIdAsync("abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(proxy);

        var result = await _controller.GetProxy("abc", CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<RemoteProxyResponse>(okResult.Value);
        Assert.Equal("TestProxy", response.Name);
    }

    [Fact]
    public async Task GetProxy_ReturnsNotFound_WhenMissing()
    {
        _mockRegistry.Setup(r => r.GetByIdAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RemoteProxy?)null);

        var result = await _controller.GetProxy("missing", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task RegisterProxy_ReturnsBadRequest_WhenNameEmpty()
    {
        var request = new RegisterRemoteProxyRequest
        {
            Name = "",
            AdminUrl = "http://localhost:8081",
            ApiKey = "key"
        };

        var result = await _controller.RegisterProxy(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task RegisterProxy_ReturnsBadRequest_WhenAdminUrlEmpty()
    {
        var request = new RegisterRemoteProxyRequest
        {
            Name = "Test",
            AdminUrl = "",
            ApiKey = "key"
        };

        var result = await _controller.RegisterProxy(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task RegisterProxy_ReturnsBadRequest_WhenAdminUrlInvalid()
    {
        var request = new RegisterRemoteProxyRequest
        {
            Name = "Test",
            AdminUrl = "not-a-url",
            ApiKey = "key"
        };

        var result = await _controller.RegisterProxy(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task RegisterProxy_ReturnsBadRequest_WhenApiKeyEmpty()
    {
        var request = new RegisterRemoteProxyRequest
        {
            Name = "Test",
            AdminUrl = "http://localhost:8081",
            ApiKey = ""
        };

        var result = await _controller.RegisterProxy(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task RegisterProxy_ReturnsBadRequest_WhenConnectivityFails()
    {
        var request = new RegisterRemoteProxyRequest
        {
            Name = "Test",
            AdminUrl = "http://localhost:8081",
            ApiKey = "key"
        };
        _mockRegistry.Setup(r => r.TestConnectivityAsync("http://localhost:8081", "key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _controller.RegisterProxy(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task RegisterProxy_ReturnsCreated_WhenValid()
    {
        var request = new RegisterRemoteProxyRequest
        {
            Name = "Test",
            AdminUrl = "http://localhost:8081",
            ApiKey = "key"
        };
        _mockRegistry.Setup(r => r.TestConnectivityAsync("http://localhost:8081", "key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockRegistry.Setup(r => r.RegisterAsync(It.IsAny<RemoteProxy>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RemoteProxy p, CancellationToken _) => p);

        var result = await _controller.RegisterProxy(request, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        var response = Assert.IsType<RemoteProxyResponse>(createdResult.Value);
        Assert.Equal("Test", response.Name);
    }

    [Fact]
    public async Task UpdateProxy_ReturnsNotFound_WhenMissing()
    {
        _mockRegistry.Setup(r => r.GetByIdAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RemoteProxy?)null);

        var result = await _controller.UpdateProxy("missing", new UpdateRemoteProxyRequest { ApiKey = "new" }, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task UpdateProxy_ReturnsOk_WhenFound()
    {
        var proxy = new RemoteProxy { Id = "abc", Name = "Test", AdminUrl = "http://localhost:8081", ApiKey = "old" };
        _mockRegistry.Setup(r => r.GetByIdAsync("abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(proxy);
        _mockRegistry.Setup(r => r.UpdateAsync(It.IsAny<RemoteProxy>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RemoteProxy p, CancellationToken _) => p);

        var result = await _controller.UpdateProxy("abc", new UpdateRemoteProxyRequest { ApiKey = "new-key" }, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task UnregisterProxy_ReturnsNoContent_WhenDeleted()
    {
        _mockRegistry.Setup(r => r.UnregisterAsync("abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _controller.UnregisterProxy("abc", CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task UnregisterProxy_ReturnsNotFound_WhenMissing()
    {
        _mockRegistry.Setup(r => r.UnregisterAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _controller.UnregisterProxy("missing", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task ForceHealthCheck_ReturnsNotFound_WhenMissing()
    {
        _mockRegistry.Setup(r => r.GetByIdAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RemoteProxy?)null);

        var result = await _controller.ForceHealthCheck("missing", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task ForceHealthCheck_SetsHealthy_WhenConnected()
    {
        var proxy = new RemoteProxy { Id = "abc", Name = "Test", AdminUrl = "http://localhost:8081", ApiKey = "key" };
        _mockRegistry.Setup(r => r.GetByIdAsync("abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(proxy);
        _mockRegistry.Setup(r => r.TestConnectivityAsync("http://localhost:8081", "key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockRegistry.Setup(r => r.UpdateAsync(It.IsAny<RemoteProxy>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RemoteProxy p, CancellationToken _) => p);

        var result = await _controller.ForceHealthCheck("abc", CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<RemoteProxyResponse>(okResult.Value);
        Assert.Equal("Healthy", response.Status);
    }

    [Fact]
    public async Task ForceHealthCheck_SetsUnhealthy_WhenDisconnected()
    {
        var proxy = new RemoteProxy { Id = "abc", Name = "Test", AdminUrl = "http://localhost:8081", ApiKey = "key" };
        _mockRegistry.Setup(r => r.GetByIdAsync("abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(proxy);
        _mockRegistry.Setup(r => r.TestConnectivityAsync("http://localhost:8081", "key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRegistry.Setup(r => r.UpdateAsync(It.IsAny<RemoteProxy>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RemoteProxy p, CancellationToken _) => p);

        var result = await _controller.ForceHealthCheck("abc", CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<RemoteProxyResponse>(okResult.Value);
        Assert.Equal("Unhealthy", response.Status);
    }
}
