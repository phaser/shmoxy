using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using shmoxy.api.data;
using Microsoft.EntityFrameworkCore;

namespace shmoxy.api.tests;

public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ApiConfig:AutoStartProxy"] = "false"
                });
            });
            builder.ConfigureServices(services =>
            {
                // Use SQLite in-memory for tests
                services.AddDbContext<ProxiesDbContext>(options =>
                    options.UseSqlite("Data Source=:memory:"));
            });
        }).CreateClient();
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsValidJson()
    {
        var response = await _client.GetAsync("/api/health");
        var content = await response.Content.ReadAsStringAsync();

        var json = JsonSerializer.Deserialize<JsonElement>(content);

        Assert.True(json.TryGetProperty("status", out var status));
        Assert.Equal("Healthy", status.GetString());

        Assert.True(json.TryGetProperty("timestamp", out var timestamp));
        Assert.NotEmpty(timestamp.GetString()!);
    }
}
