using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using shmoxy.api.models;
using shmoxy.api.models.configuration;
using shmoxy.api.server;
using shmoxy.shared.ipc;

namespace shmoxy.api.tests.server;

public class RemoteProxyHealthMonitorTests : IDisposable
{
    private readonly Mock<IRemoteProxyRegistry> _mockRegistry;
    private readonly Mock<IOptions<ApiConfig>> _mockConfig;
    private readonly ILogger<RemoteProxyHealthMonitor> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private bool _disposed;

    public RemoteProxyHealthMonitorTests()
    {
        _mockRegistry = new Mock<IRemoteProxyRegistry>();
        _mockConfig = new Mock<IOptions<ApiConfig>>();
        _mockConfig.Setup(c => c.Value).Returns(new ApiConfig { HealthCheckIntervalSeconds = 60 });
        _logger = NullLogger<RemoteProxyHealthMonitor>.Instance;
        _loggerFactory = NullLoggerFactory.Instance;
    }

    private RemoteProxyHealthMonitor CreateMonitor(
        HttpMessageHandler httpHandler,
        IRemoteProxyRegistry? registryOverride = null)
    {
        var registry = registryOverride ?? _mockRegistry.Object;

        var services = new ServiceCollection();
        services.AddSingleton(registry);
        var serviceProvider = services.BuildServiceProvider();

        var mockHttpClientFactory = new Mock<IHttpClientFactory>();
        mockHttpClientFactory
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(httpHandler));

        return new RemoteProxyHealthMonitor(
            _mockConfig.Object,
            _logger,
            serviceProvider,
            mockHttpClientFactory.Object,
            _loggerFactory);
    }

    private static FakeHttpMessageHandler CreateHealthyHandler()
    {
        return new FakeHttpMessageHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new ProxyStatus { IsListening = true, Port = 8080 }),
                System.Text.Encoding.UTF8,
                "application/json")
        });
    }

    private static FakeHttpMessageHandler CreateUnhealthyHandler()
    {
        return new FakeHttpMessageHandler(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }

    private static RemoteProxy CreateProxy(string id = "proxy-1", RemoteProxyStatus status = RemoteProxyStatus.Unknown)
    {
        return new RemoteProxy
        {
            Id = id,
            Name = "Test Proxy",
            AdminUrl = "http://localhost:5000",
            ApiKey = "test-key",
            Status = status
        };
    }

    [Fact]
    public async Task StartAsync_StartsTimerWithoutError()
    {
        using var handler = CreateHealthyHandler();
        using var monitor = CreateMonitor(handler);

        await monitor.StartAsync(CancellationToken.None);
        await monitor.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_PreventsNewHealthChecks()
    {
        using var handler = CreateHealthyHandler();
        using var monitor = CreateMonitor(handler);

        await monitor.StartAsync(CancellationToken.None);
        await monitor.StopAsync(CancellationToken.None);

        // After stopping, no further health checks should occur.
        _mockRegistry.Invocations.Clear();
        await Task.Delay(200);
        _mockRegistry.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HealthyProxy_SetsStatusToHealthy()
    {
        var proxy = CreateProxy(status: RemoteProxyStatus.Unhealthy);
        _mockRegistry.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RemoteProxy> { proxy });
        _mockRegistry.Setup(r => r.UpdateAsync(It.IsAny<RemoteProxy>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RemoteProxy p, CancellationToken _) => p);

        using var handler = CreateHealthyHandler();
        using var monitor = CreateMonitor(handler);

        await monitor.StartAsync(CancellationToken.None);

        // Wait for the initial health check (fires at TimeSpan.Zero delay)
        await Task.Delay(1000);
        await monitor.StopAsync(CancellationToken.None);

        _mockRegistry.Verify(r => r.UpdateAsync(
            It.Is<RemoteProxy>(p => p.Status == RemoteProxyStatus.Healthy),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task FailedHealthCheck_SetsStatusToUnhealthy()
    {
        var proxy = CreateProxy(status: RemoteProxyStatus.Healthy);
        _mockRegistry.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RemoteProxy> { proxy });
        _mockRegistry.Setup(r => r.UpdateAsync(It.IsAny<RemoteProxy>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RemoteProxy p, CancellationToken _) => p);

        using var handler = CreateUnhealthyHandler();
        using var monitor = CreateMonitor(handler);

        await monitor.StartAsync(CancellationToken.None);
        await Task.Delay(1000);
        await monitor.StopAsync(CancellationToken.None);

        _mockRegistry.Verify(r => r.UpdateAsync(
            It.Is<RemoteProxy>(p => p.Status == RemoteProxyStatus.Unhealthy),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task MoreThanThreeFailures_SetsStatusToUnreachable()
    {
        var proxy = CreateProxy(status: RemoteProxyStatus.Healthy);
        _mockRegistry.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RemoteProxy> { proxy });
        _mockRegistry.Setup(r => r.UpdateAsync(It.IsAny<RemoteProxy>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RemoteProxy p, CancellationToken _) => p);

        // Use a short interval to trigger multiple health checks quickly
        _mockConfig.Setup(c => c.Value).Returns(new ApiConfig { HealthCheckIntervalSeconds = 1 });

        using var handler = CreateUnhealthyHandler();
        using var monitor = CreateMonitor(handler);

        await monitor.StartAsync(CancellationToken.None);

        // Wait long enough for at least 4 health checks (initial + 3 interval ticks)
        await Task.Delay(4500);
        await monitor.StopAsync(CancellationToken.None);

        _mockRegistry.Verify(r => r.UpdateAsync(
            It.Is<RemoteProxy>(p => p.Status == RemoteProxyStatus.Unreachable),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task SuccessAfterFailures_ResetsToHealthy()
    {
        var proxy = CreateProxy(status: RemoteProxyStatus.Unhealthy);

        _mockRegistry.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RemoteProxy> { proxy });
        _mockRegistry.Setup(r => r.UpdateAsync(It.IsAny<RemoteProxy>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RemoteProxy p, CancellationToken _) => p);

        // First two calls fail, then succeed
        var callIndex = 0;
        var handler = new FakeHttpMessageHandler(() =>
        {
            var idx = Interlocked.Increment(ref callIndex);
            if (idx <= 2)
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new ProxyStatus { IsListening = true, Port = 8080 }),
                    System.Text.Encoding.UTF8,
                    "application/json")
            };
        });

        _mockConfig.Setup(c => c.Value).Returns(new ApiConfig { HealthCheckIntervalSeconds = 1 });

        using var monitor = CreateMonitor(handler);

        await monitor.StartAsync(CancellationToken.None);
        await Task.Delay(3500);
        await monitor.StopAsync(CancellationToken.None);

        // After recovery, Healthy status should have been set
        _mockRegistry.Verify(r => r.UpdateAsync(
            It.Is<RemoteProxy>(p => p.Status == RemoteProxyStatus.Healthy),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExceptionInHealthCheck_IsCaughtAndDoesNotThrow()
    {
        _mockRegistry.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Registry failure"));

        using var handler = CreateHealthyHandler();
        using var monitor = CreateMonitor(handler);

        await monitor.StartAsync(CancellationToken.None);

        // Should not throw — exception is caught internally
        await Task.Delay(500);
        await monitor.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void Dispose_IsSafeToCallMultipleTimes()
    {
        using var handler = CreateHealthyHandler();
        var monitor = CreateMonitor(handler);

        monitor.Dispose();
        monitor.Dispose(); // Double dispose should be safe
    }

    [Fact]
    public async Task NoProxies_DoesNotCallUpdate()
    {
        _mockRegistry.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RemoteProxy>());

        using var handler = CreateHealthyHandler();
        using var monitor = CreateMonitor(handler);

        await monitor.StartAsync(CancellationToken.None);
        await Task.Delay(500);
        await monitor.StopAsync(CancellationToken.None);

        _mockRegistry.Verify(r => r.UpdateAsync(It.IsAny<RemoteProxy>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}

/// <summary>
/// Fake HttpMessageHandler that returns a new response from a factory for each request.
/// Uses a factory function to produce fresh HttpResponseMessage instances per call,
/// avoiding the "Cannot access a disposed object" error from reusing response objects.
/// </summary>
internal class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpResponseMessage> _responseFactory;

    public FakeHttpMessageHandler(Func<HttpResponseMessage> responseFactory)
    {
        _responseFactory = responseFactory;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(_responseFactory());
    }
}
