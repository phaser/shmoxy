using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using shmoxy.api.Controllers;
using shmoxy.api.ipc;
using shmoxy.api.server;

namespace shmoxy.api.tests.Controllers;

public class BreakpointsControllerTests
{
    private readonly Mock<IProxyProcessManager> _mockProcessManager;
    private readonly Mock<ILogger<BreakpointsController>> _mockLogger;
    private readonly Mock<IProxyIpcClient> _mockIpcClient;
    private readonly BreakpointsController _controller;

    public BreakpointsControllerTests()
    {
        _mockProcessManager = new Mock<IProxyProcessManager>();
        _mockLogger = new Mock<ILogger<BreakpointsController>>();
        _mockIpcClient = new Mock<IProxyIpcClient>();
        _controller = new BreakpointsController(_mockProcessManager.Object, _mockLogger.Object);

        _mockProcessManager.Setup(m => m.GetIpcClient()).Returns(_mockIpcClient.Object);
    }

    [Fact]
    public async Task Enable_CallsIpcClient_ReturnsOk()
    {
        _mockIpcClient.Setup(m => m.EnableBreakpointsAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _controller.Enable(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
        _mockIpcClient.Verify(m => m.EnableBreakpointsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Disable_CallsIpcClient_ReturnsOk()
    {
        _mockIpcClient.Setup(m => m.DisableBreakpointsAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _controller.Disable(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
        _mockIpcClient.Verify(m => m.DisableBreakpointsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetPaused_ReturnsJsonContent()
    {
        var pausedJson = "[{\"correlationId\":\"abc\",\"method\":\"GET\"}]";
        _mockIpcClient.Setup(m => m.GetPausedRequestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(pausedJson);

        var result = await _controller.GetPaused(CancellationToken.None);

        var contentResult = Assert.IsType<ContentResult>(result);
        Assert.Equal("application/json", contentResult.ContentType);
        Assert.Equal(pausedJson, contentResult.Content);
    }

    [Fact]
    public async Task GetPaused_EmptyList_ReturnsEmptyJson()
    {
        _mockIpcClient.Setup(m => m.GetPausedRequestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("[]");

        var result = await _controller.GetPaused(CancellationToken.None);

        var contentResult = Assert.IsType<ContentResult>(result);
        Assert.Equal("[]", contentResult.Content);
    }

    [Fact]
    public async Task Release_WithoutBody_CallsReleaseWithNullBody()
    {
        _mockIpcClient.Setup(m => m.ReleaseRequestAsync("corr-1", null, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.ContentLength = 0;
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        var result = await _controller.Release("corr-1", CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
        _mockIpcClient.Verify(m => m.ReleaseRequestAsync("corr-1", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Release_WithBody_CallsReleaseWithBody()
    {
        var modifiedBody = "{\"method\":\"POST\",\"url\":\"http://example.com\"}";

        _mockIpcClient.Setup(m => m.ReleaseRequestAsync("corr-2", modifiedBody, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(modifiedBody));
        httpContext.Request.ContentLength = modifiedBody.Length;
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        var result = await _controller.Release("corr-2", CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
        _mockIpcClient.Verify(m => m.ReleaseRequestAsync("corr-2", modifiedBody, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Drop_CallsIpcClient_ReturnsOk()
    {
        _mockIpcClient.Setup(m => m.DropRequestAsync("corr-3", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _controller.Drop("corr-3", CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
        _mockIpcClient.Verify(m => m.DropRequestAsync("corr-3", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetRules_ReturnsJsonContent()
    {
        var rulesJson = "[{\"id\":\"rule-1\",\"method\":\"GET\",\"urlPattern\":\"/api\"}]";
        _mockIpcClient.Setup(m => m.GetBreakpointRulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(rulesJson);

        var result = await _controller.GetRules(CancellationToken.None);

        var contentResult = Assert.IsType<ContentResult>(result);
        Assert.Equal("application/json", contentResult.ContentType);
        Assert.Equal(rulesJson, contentResult.Content);
    }

    [Fact]
    public async Task AddRule_CallsIpcClient_ReturnsJsonContent()
    {
        var ruleJson = "{\"id\":\"rule-new\",\"method\":\"POST\",\"urlPattern\":\"/api/test\"}";
        _mockIpcClient.Setup(m => m.AddBreakpointRuleAsync("POST", "/api/test", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ruleJson);

        var request = new AddBreakpointRuleRequest { Method = "POST", UrlPattern = "/api/test" };

        var result = await _controller.AddRule(request, CancellationToken.None);

        var contentResult = Assert.IsType<ContentResult>(result);
        Assert.Equal("application/json", contentResult.ContentType);
        Assert.Equal(ruleJson, contentResult.Content);
    }

    [Fact]
    public async Task AddRule_WithNullMethod_CallsIpcClient()
    {
        var ruleJson = "{\"id\":\"rule-2\",\"method\":null,\"urlPattern\":\"/api\"}";
        _mockIpcClient.Setup(m => m.AddBreakpointRuleAsync(null, "/api", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ruleJson);

        var request = new AddBreakpointRuleRequest { Method = null, UrlPattern = "/api" };

        var result = await _controller.AddRule(request, CancellationToken.None);

        var contentResult = Assert.IsType<ContentResult>(result);
        Assert.Equal(ruleJson, contentResult.Content);
    }

    [Fact]
    public async Task RemoveRule_CallsIpcClient_ReturnsOk()
    {
        _mockIpcClient.Setup(m => m.RemoveBreakpointRuleAsync("rule-1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _controller.RemoveRule("rule-1", CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
        _mockIpcClient.Verify(m => m.RemoveBreakpointRuleAsync("rule-1", It.IsAny<CancellationToken>()), Times.Once);
    }
}
