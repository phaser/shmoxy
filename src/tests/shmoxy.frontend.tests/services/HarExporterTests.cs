using System.Text.Json;
using shmoxy.frontend.services;
using Xunit;

namespace shmoxy.frontend.tests.services;

public class HarExporterTests
{
    [Fact]
    public void Export_EmptyRows_ReturnsValidHarWithNoEntries()
    {
        var result = HarExporter.Export(new List<InspectionRow>());

        Assert.Contains("\"version\": \"1.2\"", result);
        Assert.Contains("\"entries\": []", result);
    }

    [Fact]
    public void Export_SingleRow_ContainsRequestAndResponse()
    {
        var rows = new List<InspectionRow>
        {
            new()
            {
                Method = "GET",
                Url = "https://example.com/api?foo=bar",
                Timestamp = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
                Duration = TimeSpan.FromMilliseconds(150),
                StatusCode = 200,
                RequestHeaders = new List<KeyValuePair<string, string>>
                {
                    new("Host", "example.com"),
                    new("Accept", "application/json")
                },
                ResponseHeaders = new List<KeyValuePair<string, string>>
                {
                    new("Content-Type", "application/json")
                },
                ResponseBody = "{\"ok\":true}",
                ResponseContentType = "application/json"
            }
        };

        var result = HarExporter.Export(rows);

        Assert.Contains("\"method\": \"GET\"", result);
        Assert.Contains("\"url\": \"https://example.com/api?foo=bar\"", result);
        Assert.Contains("\"status\": 200", result);
        Assert.Contains("\"name\": \"foo\"", result);
        Assert.Contains("\"value\": \"bar\"", result);
        Assert.Contains("\"mimeType\": \"application/json\"", result);
    }

    [Fact]
    public void Export_PostWithBody_IncludesPostData()
    {
        var rows = new List<InspectionRow>
        {
            new()
            {
                Method = "POST",
                Url = "https://example.com/api",
                Timestamp = DateTime.UtcNow,
                StatusCode = 201,
                RequestHeaders = new List<KeyValuePair<string, string>>
                {
                    new("Content-Type", "application/json")
                },
                RequestBody = "{\"name\":\"test\"}",
                ResponseHeaders = new List<KeyValuePair<string, string>>(),
                ResponseBody = "Created",
                ResponseContentType = "text/plain"
            }
        };

        var result = HarExporter.Export(rows);

        var doc = JsonDocument.Parse(result);
        var entry = doc.RootElement.GetProperty("log").GetProperty("entries")[0];
        var postData = entry.GetProperty("request").GetProperty("postData");
        Assert.Equal("{\"name\":\"test\"}", postData.GetProperty("text").GetString());
        Assert.Equal("application/json", postData.GetProperty("mimeType").GetString());
    }

    [Fact]
    public void Export_BinaryResponse_UsesBase64Encoding()
    {
        var rows = new List<InspectionRow>
        {
            new()
            {
                Method = "GET",
                Url = "https://example.com/image.png",
                Timestamp = DateTime.UtcNow,
                StatusCode = 200,
                RequestHeaders = new List<KeyValuePair<string, string>>(),
                ResponseHeaders = new List<KeyValuePair<string, string>>(),
                ResponseBody = "[Image: image/png, 100 bytes]",
                ResponseBodyBase64 = "iVBORw0KGgo=",
                ResponseContentType = "image/png"
            }
        };

        var result = HarExporter.Export(rows);

        Assert.Contains("\"encoding\": \"base64\"", result);
        Assert.Contains("iVBORw0KGgo=", result);
    }

    [Fact]
    public void Export_WebSocketRows_AreExcluded()
    {
        var rows = new List<InspectionRow>
        {
            new()
            {
                Method = "GET",
                Url = "wss://example.com/ws",
                Timestamp = DateTime.UtcNow,
                IsWebSocket = true,
                RequestHeaders = new List<KeyValuePair<string, string>>(),
                ResponseHeaders = new List<KeyValuePair<string, string>>()
            }
        };

        var result = HarExporter.Export(rows);

        Assert.Contains("\"entries\": []", result);
    }
}
