using shmoxy.frontend.services;
using Xunit;

namespace shmoxy.frontend.tests.services;

public class CurlExporterTests
{
    [Fact]
    public void GenerateCommand_GetRequest_OmitsMethodFlag()
    {
        var row = new InspectionRow
        {
            Method = "GET",
            Url = "https://example.com/api",
            RequestHeaders = new List<KeyValuePair<string, string>>()
        };

        var result = CurlExporter.GenerateCommand(row);

        Assert.StartsWith("curl 'https://example.com/api'", result);
        Assert.DoesNotContain("-X", result);
    }

    [Fact]
    public void GenerateCommand_PostRequest_IncludesMethodFlag()
    {
        var row = new InspectionRow
        {
            Method = "POST",
            Url = "https://example.com/api",
            RequestHeaders = new List<KeyValuePair<string, string>>(),
            RequestBody = "{\"key\":\"value\"}"
        };

        var result = CurlExporter.GenerateCommand(row);

        Assert.Contains("-X POST", result);
        Assert.Contains("-d", result);
        Assert.Contains("{\"key\":\"value\"}", result);
    }

    [Fact]
    public void GenerateCommand_IncludesHeaders()
    {
        var row = new InspectionRow
        {
            Method = "GET",
            Url = "https://example.com",
            RequestHeaders = new List<KeyValuePair<string, string>>
            {
                new("Content-Type", "application/json"),
                new("Authorization", "Bearer token123")
            }
        };

        var result = CurlExporter.GenerateCommand(row);

        Assert.Contains("-H 'Content-Type: application/json'", result);
        Assert.Contains("-H 'Authorization: Bearer token123'", result);
    }

    [Fact]
    public void GenerateCommand_EscapesSingleQuotes()
    {
        var row = new InspectionRow
        {
            Method = "GET",
            Url = "https://example.com/api?q=it's",
            RequestHeaders = new List<KeyValuePair<string, string>>()
        };

        var result = CurlExporter.GenerateCommand(row);

        Assert.Contains("it'\\''s", result);
    }

    [Fact]
    public void GenerateCommand_NoBody_OmitsDataFlag()
    {
        var row = new InspectionRow
        {
            Method = "DELETE",
            Url = "https://example.com/resource/1",
            RequestHeaders = new List<KeyValuePair<string, string>>()
        };

        var result = CurlExporter.GenerateCommand(row);

        Assert.DoesNotContain("-d", result);
    }
}
