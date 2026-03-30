using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using shmoxy.api.ipc;
using shmoxy.api.models;
using shmoxy.api.server;
using shmoxy.shared.ipc;

namespace shmoxy.api.tests.Integration;

public class ConfigIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public ConfigIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
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

    [Fact]
    public async Task GetConfig_ProxyNotRunning_ReturnsDefaultConfig()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/proxies/local/config");

        // Assert — returns persisted config or defaults when proxy is stopped
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var config = JsonSerializer.Deserialize<ProxyConfig>(
            await response.Content.ReadAsStringAsync(), JsonOptions);
        Assert.NotNull(config);
    }

    [Fact]
    public async Task GetConfig_RemoteProxyNotFound_Returns400()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/proxies/nonexistent-guid/config");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateConfig_InvalidPort_Returns400()
    {
        // Arrange
        var client = _factory.CreateClient();
        var config = new ProxyConfig { Port = 99999, LogLevel = ProxyConfig.LogLevelEnum.Info };
        var content = new StringContent(JsonSerializer.Serialize(config), Encoding.UTF8, "application/json");

        // Act
        var response = await client.PutAsync("/api/proxies/local/config", content);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.Contains("Port", responseBody);
    }

    [Fact]
    public async Task UpdateConfig_InvalidLogLevel_Returns400()
    {
        // Arrange
        var client = _factory.CreateClient();
        var config = new ProxyConfig { Port = 8080, LogLevel = (ProxyConfig.LogLevelEnum)999 };
        var content = new StringContent(JsonSerializer.Serialize(config), Encoding.UTF8, "application/json");

        // Act
        var response = await client.PutAsync("/api/proxies/local/config", content);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.Contains("LogLevel", responseBody);
    }

    [Fact]
    public async Task UpdateConfig_Validation_PassesForValidConfig()
    {
        // Arrange
        var client = _factory.CreateClient();
        var config = new ProxyConfig { Port = 9090, LogLevel = ProxyConfig.LogLevelEnum.Debug };
        var content = new StringContent(JsonSerializer.Serialize(config), Encoding.UTF8, "application/json");

        // Act - Will fail because proxy isn't running, but validates config first
        var response = await client.PutAsync("/api/proxies/local/config", content);

        // Assert - Should fail with "proxy not running" not "invalid config"
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Port", responseBody);
        Assert.DoesNotContain("LogLevel", responseBody);
        Assert.Contains("running", responseBody);
    }

}
