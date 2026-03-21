using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using shmoxy.api.Controllers;
using shmoxy.api.models;
using shmoxy.api.server;

namespace shmoxy.api.tests.Controllers;

public class CertsControllerTests
{
    private readonly Mock<IProxyProcessManager> _mockProcessManager;
    private readonly Mock<IRemoteProxyRegistry> _mockRegistry;
    private readonly Mock<ILogger<CertsController>> _mockLogger;
    private readonly CertsController _controller;

    public CertsControllerTests()
    {
        _mockProcessManager = new Mock<IProxyProcessManager>();
        _mockRegistry = new Mock<IRemoteProxyRegistry>();
        _mockLogger = new Mock<ILogger<CertsController>>();
        _controller = new CertsController(_mockProcessManager.Object, _mockRegistry.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task GetLocalCert_Pem_ReturnsCertificate()
    {
        _mockProcessManager.Setup(m => m.GetStateAsync())
            .ReturnsAsync(new ProxyInstanceState { State = ProxyProcessState.Running });
        _mockProcessManager.Setup(m => m.GetRootCertPemAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("-----BEGIN CERTIFICATE-----");

        var result = await _controller.GetRootCertificate("local", "pem");

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal("text/plain", fileResult.ContentType);
        Assert.Contains("shmoxy-root-ca.pem", fileResult.FileDownloadName);
    }

    [Fact]
    public async Task GetLocalCert_Der_ReturnsCertificate()
    {
        _mockProcessManager.Setup(m => m.GetStateAsync())
            .ReturnsAsync(new ProxyInstanceState { State = ProxyProcessState.Running });
        _mockProcessManager.Setup(m => m.GetRootCertDerAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 0x30, 0x82 });

        var result = await _controller.GetRootCertificate("local", "der");

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/x-x509-ca-cert", fileResult.ContentType);
        Assert.Contains("shmoxy-root-ca.der", fileResult.FileDownloadName);
    }

    [Fact]
    public async Task GetLocalCert_InvalidType_Returns400()
    {
        var result = await _controller.GetRootCertificate("local", "pfx");

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Invalid type", badRequest.Value.ToString());
    }

    [Fact]
    public async Task GetLocalCert_ProxyNotRunning_Returns400()
    {
        _mockProcessManager.Setup(m => m.GetStateAsync())
            .ReturnsAsync(new ProxyInstanceState { State = ProxyProcessState.Stopped });

        var result = await _controller.GetRootCertificate("local", "pem");

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("must be running", badRequest.Value.ToString());
    }

    [Fact]
    public async Task GetRemoteCert_RemoteUnavailable_Returns502()
    {
        var proxy = new RemoteProxy
        {
            Id = "test-id",
            Name = "test",
            AdminUrl = "http://invalid:9090",
            ApiKey = "test-key"
        };
        _mockRegistry.Setup(m => m.GetByIdAsync("test-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(proxy);

        var result = await _controller.GetRootCertificate("test-id", "pem");

        var badGateway = Assert.IsType<ObjectResult>(result);
        Assert.Equal(502, badGateway.StatusCode);
    }

    [Fact]
    public async Task GetRemoteCert_ProxyNotFound_Returns404()
    {
        _mockRegistry.Setup(m => m.GetByIdAsync("nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RemoteProxy?)null);

        var result = await _controller.GetRootCertificate("nonexistent", "pem");

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Contains("not found", notFound.Value.ToString());
    }
}
