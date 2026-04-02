using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using shmoxy.api.data;
using shmoxy.api.models.dto;

namespace shmoxy.api.tests.Integration;

/// <summary>
/// End-to-end test that verifies session save works even when the database
/// was created before the InspectionSessions tables were added.
/// Reproduces: https://github.com/phaser/shmoxy/issues/97
/// </summary>
public class SessionSaveE2eTests : IDisposable
{
    private readonly string _dbPath;

    public SessionSaveE2eTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"shmoxy-test-{Guid.NewGuid()}.db");
    }

    /// <summary>
    /// Creates a SQLite database with only the RemoteProxies table,
    /// simulating a database that was created before InspectionSessions was added.
    /// </summary>
    private void CreateLegacyDatabase()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE ""RemoteProxies"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_RemoteProxies"" PRIMARY KEY,
                ""Name"" TEXT NOT NULL,
                ""AdminUrl"" TEXT NOT NULL,
                ""ApiKey"" TEXT NOT NULL,
                ""Status"" INTEGER NOT NULL
            );
            CREATE UNIQUE INDEX ""IX_RemoteProxies_Name"" ON ""RemoteProxies"" (""Name"");
        ";
        command.ExecuteNonQuery();
    }

    private WebApplicationFactory<Program> CreateFactory()
    {
        var connectionString = $"Data Source={_dbPath}";
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ApiConfig:AutoStartProxy"] = "false",
                    ["ApiConfig:ConnectionString"] = connectionString
                });
            });
            builder.ConfigureServices(services =>
            {
                // Remove the default DbContext registration so we use our test database.
                // Program.cs reads the connection string eagerly before WebApplicationFactory
                // config overrides are applied, so we must replace the service registration.
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<ProxiesDbContext>));
                if (descriptor != null)
                    services.Remove(descriptor);

                services.AddDbContext<ProxiesDbContext>(options =>
                    options.UseSqlite(connectionString));
            });
        });
    }

    [Fact]
    public async Task SaveSession_WithPreExistingDatabase_Succeeds()
    {
        // Arrange — create a legacy DB that is missing the InspectionSessions tables
        CreateLegacyDatabase();

        using var factory = CreateFactory();
        var client = factory.CreateClient();

        // Simulate proxied traffic data (what the UI captures and sends on Save)
        var sessionRequest = new CreateSessionRequest
        {
            Name = "Test Proxy Session",
            Rows =
            [
                new SessionRowDto
                {
                    Method = "GET",
                    Url = "https://example.com/api/data",
                    StatusCode = 200,
                    DurationMs = 150,
                    Timestamp = DateTime.UtcNow,
                    RequestHeaders = new List<KeyValuePair<string, string>>
                    {
                        new("Host", "example.com"),
                        new("Accept", "application/json")
                    },
                    ResponseHeaders = new List<KeyValuePair<string, string>>
                    {
                        new("Content-Type", "application/json"),
                        new("Content-Length", "42")
                    },
                    ResponseBody = "{\"status\": \"ok\"}"
                },
                new SessionRowDto
                {
                    Method = "POST",
                    Url = "https://example.com/api/submit",
                    StatusCode = 201,
                    DurationMs = 300,
                    Timestamp = DateTime.UtcNow,
                    RequestHeaders = new List<KeyValuePair<string, string>>
                    {
                        new("Host", "example.com"),
                        new("Content-Type", "application/json")
                    },
                    RequestBody = "{\"key\": \"value\"}",
                    ResponseHeaders = new List<KeyValuePair<string, string>>
                    {
                        new("Content-Type", "application/json")
                    },
                    ResponseBody = "{\"id\": 1}"
                },
                new SessionRowDto
                {
                    Method = "GET",
                    Url = "https://example.com/health",
                    StatusCode = 200,
                    DurationMs = 50,
                    Timestamp = DateTime.UtcNow,
                    RequestHeaders = new List<KeyValuePair<string, string>>
                    {
                        new("Host", "example.com")
                    },
                    ResponseHeaders = new List<KeyValuePair<string, string>>
                    {
                        new("Content-Type", "text/plain")
                    },
                    ResponseBody = "OK"
                }
            ]
        };

        // Act — save the session (this is what the Save button does in the UI)
        var response = await client.PostAsJsonAsync("/api/sessions", sessionRequest);

        // Assert — should succeed (was failing with 500 due to missing InspectionSessions table)
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var sessionResponse = await response.Content.ReadFromJsonAsync<SessionResponse>();
        Assert.NotNull(sessionResponse);
        Assert.Equal("Test Proxy Session", sessionResponse.Name);
        Assert.Equal(3, sessionResponse.RowCount);

        // Verify we can retrieve the saved session rows
        var getResponse = await client.GetAsync($"/api/sessions/{sessionResponse.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var rows = await getResponse.Content.ReadFromJsonAsync<List<SessionRowDto>>();
        Assert.NotNull(rows);
        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public async Task SaveSession_WithFreshDatabase_Succeeds()
    {
        // Arrange — no pre-existing database; EnsureCreated should handle everything
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var sessionRequest = new CreateSessionRequest
        {
            Name = "Fresh DB Session",
            Rows =
            [
                new SessionRowDto
                {
                    Method = "GET",
                    Url = "https://example.com/",
                    StatusCode = 200,
                    DurationMs = 100,
                    Timestamp = DateTime.UtcNow,
                    ResponseBody = "<html>Hello</html>"
                }
            ]
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/sessions", sessionRequest);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var sessionResponse = await response.Content.ReadFromJsonAsync<SessionResponse>();
        Assert.NotNull(sessionResponse);
        Assert.Equal("Fresh DB Session", sessionResponse.Name);
        Assert.Equal(1, sessionResponse.RowCount);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            SqliteConnection.ClearAllPools();
            File.Delete(_dbPath);
        }
    }
}
