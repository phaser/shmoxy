using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using shmoxy.api.models;
using shmoxy.api.models.dto;
using shmoxy.api.server;

namespace shmoxy.api.tests.Integration;

public class SessionIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _baseFactory;

    public SessionIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _baseFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ApiConfig:AutoStartProxy"] = "false"
                });
            });
        });
    }

    private WebApplicationFactory<Program> CreateFactoryWithMockRepo(Mock<ISessionRepository> mockRepo)
    {
        return _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ISessionRepository));
                if (descriptor != null)
                    services.Remove(descriptor);
                services.AddSingleton(mockRepo.Object);
            });
        });
    }

    [Fact]
    public async Task CreateSession_AcceptsLargePayload()
    {
        // Arrange - mock repo to return a valid session
        var mockRepo = new Mock<ISessionRepository>();
        mockRepo.Setup(r => r.CreateSessionAsync(
            It.IsAny<string>(), It.IsAny<List<InspectionSessionRow>>(), It.IsAny<List<InspectionSessionLogEntry>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InspectionSession { Id = "large-1", Name = "Large Session", RowCount = 35 });

        var factory = CreateFactoryWithMockRepo(mockRepo);
        var client = factory.CreateClient();

        // Build a payload exceeding Kestrel's default 30MB limit
        var largeBody = new string('x', 1_000_000); // 1MB per row body
        var rows = Enumerable.Range(0, 35).Select(i => new SessionRowDto
        {
            Method = "GET",
            Url = $"https://example.com/{i}",
            Timestamp = DateTime.UtcNow,
            RequestBody = largeBody,
            ResponseBody = largeBody
        }).ToList();

        var request = new CreateSessionRequest
        {
            Name = "Large Session",
            Rows = rows
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/sessions", request);

        // Assert - should succeed, not 413
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task UpdateSession_AcceptsLargePayload()
    {
        // Arrange
        var mockRepo = new Mock<ISessionRepository>();
        var session = new InspectionSession { Id = "update-1", Name = "Session To Update", RowCount = 1 };
        mockRepo.Setup(r => r.GetSessionAsync("update-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var factory = CreateFactoryWithMockRepo(mockRepo);
        var client = factory.CreateClient();

        // Build a payload exceeding Kestrel's default 30MB limit
        var largeBody = new string('x', 1_000_000);
        var updateRequest = new UpdateSessionRequest
        {
            Rows = Enumerable.Range(0, 35).Select(i => new SessionRowDto
            {
                Method = "POST",
                Url = $"https://example.com/{i}",
                Timestamp = DateTime.UtcNow,
                RequestBody = largeBody,
                ResponseBody = largeBody
            }).ToList()
        };

        // Act
        var response = await client.PutAsJsonAsync("/api/sessions/update-1", updateRequest);

        // Assert - should succeed, not 413
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
