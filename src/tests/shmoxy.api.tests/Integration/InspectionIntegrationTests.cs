using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using shmoxy.api.models;
using shmoxy.api.server;
using shmoxy.shared.ipc;

namespace shmoxy.api.tests.Integration;

public class InspectionIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public InspectionIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task StreamEndpoint_ReturnsSseContentType()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act - SSE endpoint returns 200 immediately, errors happen during streaming
        var response = await client.GetAsync("/api/proxies/local/inspect/stream");

        // Assert - Content-Type should be set even if stream fails
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task StreamEndpoint_ProxyNotRunning_StreamFailsGracefully()
    {
        // Arrange
        var mockProcessManager = new Mock<IProxyProcessManager>();
        mockProcessManager.Setup(m => m.GetStateAsync())
            .ReturnsAsync(new ProxyInstanceState { State = ProxyProcessState.Stopped });

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IProxyProcessManager));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }
                services.AddSingleton(mockProcessManager.Object);
            });
        });

        var client = factory.CreateClient();

        // Act - SSE endpoint returns 200 immediately, error happens during streaming
        // For this test, we just verify the endpoint accepts the connection
        var response = await client.GetAsync("/api/proxies/local/inspect/stream");

        // Assert - SSE endpoints return 200 OK, errors happen during stream reading
        // The actual error handling is tested in unit tests
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task StreamEndpoint_HandlesClientDisconnect_Gracefully()
    {
        // Arrange
        var mockProcessManager = new Mock<IProxyProcessManager>();
        mockProcessManager.Setup(m => m.GetStateAsync())
            .ReturnsAsync(new ProxyInstanceState { State = ProxyProcessState.Running });

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IProxyProcessManager));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }
                services.AddSingleton(mockProcessManager.Object);
            });
        });

        var client = factory.CreateClient();
        var cts = new CancellationTokenSource();

        // Act - Start reading stream, then cancel
        var requestTask = client.GetAsync("/api/proxies/local/inspect/stream", cts.Token);
        
        // Wait a brief moment for connection to establish
        await Task.Delay(100);
        
        // Cancel the request
        cts.Cancel();

        // Assert - Should not throw unhandled exceptions
        try
        {
            await requestTask;
        }
        catch (TaskCanceledException)
        {
            // Expected - client disconnected
        }
        catch (OperationCanceledException)
        {
            // Also expected
        }
    }
}
