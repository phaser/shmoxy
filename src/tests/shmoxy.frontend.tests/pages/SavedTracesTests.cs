using shmoxy.frontend.models;
using shmoxy.frontend.pages;
using Xunit;

namespace shmoxy.frontend.tests.pages;

public class SavedTracesTests
{
    private static SavedTraceSummary MakeSummary() => new()
    {
        Id = "trace-1",
        Method = "POST",
        Url = "https://example.com/api/login",
        StatusCode = 401,
        Note = "auth token expired here"
    };

    [Theory]
    [InlineData("", true)]
    [InlineData("login", true)]
    [InlineData("LOGIN", true)]
    [InlineData("post", true)]
    [InlineData("401", true)]
    [InlineData("token expired", true)]
    [InlineData("does-not-match", false)]
    public void MatchesSearch_FiltersByUrlMethodStatusAndNote(string query, bool expected)
    {
        Assert.Equal(expected, SavedTraces.MatchesSearch(MakeSummary(), query));
    }

    [Fact]
    public void MatchesSearch_HandlesMissingStatusAndNote()
    {
        var summary = new SavedTraceSummary { Method = "GET", Url = "https://example.com" };

        Assert.False(SavedTraces.MatchesSearch(summary, "404"));
        Assert.True(SavedTraces.MatchesSearch(summary, "example"));
    }

    [Fact]
    public void ToInspectionRow_MapsAllFields()
    {
        var data = new SavedTraceData
        {
            Id = "trace-9",
            Method = "GET",
            Url = "https://example.com/data",
            StatusCode = 200,
            DurationMs = 250,
            Timestamp = new DateTime(2026, 7, 15, 10, 0, 0, DateTimeKind.Utc),
            RequestHeaders = [new("Host", "example.com")],
            ResponseHeaders = [new("Content-Type", "application/json")],
            RequestBody = "req",
            ResponseBody = "resp",
            ResponseContentType = "application/json",
            TimingSendMs = 1.5,
            TimingWaitMs = 100,
            TimingReceiveMs = 5
        };

        var row = SavedTraces.ToInspectionRow(data, 3);

        Assert.Equal(3, row.Id);
        Assert.Equal("trace-9", row.SavedTraceId);
        Assert.Equal("GET", row.Method);
        Assert.Equal(TimeSpan.FromMilliseconds(250), row.Duration);
        Assert.Equal("Host", row.RequestHeaders[0].Key);
        Assert.Equal("resp", row.ResponseBody);
        Assert.Equal(4, row.ResponseBodySize);
        Assert.NotNull(row.Timing);
        Assert.Equal(1.5, row.Timing.SendMs);
    }

    [Fact]
    public void ToInspectionRow_MapsWebSocketFrames()
    {
        var data = new SavedTraceData
        {
            Id = "trace-ws",
            Method = "GET",
            Url = "wss://example.com/socket",
            IsWebSocket = true,
            WebSocketClosed = true,
            WebSocketFrames =
            [
                new WebSocketFrameInfo { Direction = "client", FrameType = "text", Payload = "hello" }
            ]
        };

        var row = SavedTraces.ToInspectionRow(data, 1);

        Assert.True(row.IsWebSocket);
        Assert.True(row.WebSocketClosed);
        Assert.Single(row.WebSocketFrames);
        Assert.Equal("hello", row.WebSocketFrames[0].Payload);
    }
}
