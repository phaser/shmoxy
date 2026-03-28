using System.Text.Json;
using shmoxy.frontend.models;
using Xunit;

namespace shmoxy.frontend.tests.models;

public class InspectionEventDtoTests
{
    [Fact]
    public void Deserialize_ResponseEvent_HasHeadersAndBody()
    {
        // Simulate JSON from the API (PascalCase, byte[] as Base64)
        var json = """{"Timestamp":"2024-01-01T00:00:00Z","EventType":"response","Method":"","Url":"","StatusCode":200,"Headers":{"Content-Type":"text/html"},"Body":"SGVM"}""";

        var dto = JsonSerializer.Deserialize<InspectionEventDto>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(dto);
        Assert.Equal("response", dto.EventType);
        Assert.Equal(200, dto.StatusCode);
        Assert.NotNull(dto.Headers);
        Assert.Single(dto.Headers);
        Assert.Equal("text/html", dto.Headers["Content-Type"]);
        Assert.NotNull(dto.Body);
        Assert.True(dto.Body.Length > 0);
    }

    [Fact]
    public void Deserialize_ResponseEvent_WithoutHeadersAndBody_DefaultsToNull()
    {
        // JSON without Headers and Body fields
        var json = """{"Timestamp":"2024-01-01T00:00:00Z","EventType":"response","Method":"","Url":"","StatusCode":200}""";

        var dto = JsonSerializer.Deserialize<InspectionEventDto>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(dto);
        Assert.Null(dto.Headers);
        Assert.Null(dto.Body);
    }

    [Fact]
    public void Deserialize_RoundTrip_PreservesData()
    {
        // Simulate the full chain: InspectionEvent → JSON → InspectionEventDto
        var inspectionEvent = new shmoxy.shared.ipc.InspectionEvent
        {
            Timestamp = DateTime.UtcNow,
            EventType = "response",
            Method = "",
            Url = "",
            StatusCode = 200,
            Headers = new Dictionary<string, string>
            {
                { "Content-Type", "text/html" },
                { "Server", "nginx" }
            },
            Body = System.Text.Encoding.UTF8.GetBytes("<html>Hello</html>")
        };

        // Serialize as the API would (default settings)
        var json = JsonSerializer.Serialize(inspectionEvent);

        // Deserialize as the frontend would
        var dto = JsonSerializer.Deserialize<InspectionEventDto>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(dto);
        Assert.Equal(200, dto.StatusCode);
        Assert.NotNull(dto.Headers);
        Assert.Equal(2, dto.Headers.Count);
        Assert.Equal("text/html", dto.Headers["Content-Type"]);
        Assert.NotNull(dto.Body);
        Assert.Equal("<html>Hello</html>", System.Text.Encoding.UTF8.GetString(dto.Body));
    }
}
