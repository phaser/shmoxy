using Microsoft.Extensions.Hosting;
using shmoxy.frontend.models;
using shmoxy.frontend.services;
using shmoxy.shared.ipc;
using Xunit;

namespace shmoxy.frontend.tests.services;

public class InspectionDataServiceTests
{
    private static InspectionDataService CreateService(IHostApplicationLifetime? lifetime = null)
    {
        var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost") };
        var apiClient = new ApiClient(httpClient);
        return new InspectionDataService(apiClient, lifetime);
    }

    [Fact]
    public void GetRows_ReturnsEmptyList_Initially()
    {
        using var service = CreateService();

        var rows = service.GetRows();

        Assert.Empty(rows);
    }

    [Fact]
    public void Clear_RemovesAllRows()
    {
        using var service = CreateService();
        var changed = false;
        service.OnRowsChanged += () => changed = true;

        service.Clear();

        Assert.Empty(service.GetRows());
        Assert.True(changed);
    }

    [Fact]
    public void GetRows_ReturnsCopy_NotReference()
    {
        using var service = CreateService();

        var rows1 = service.GetRows();
        var rows2 = service.GetRows();

        Assert.NotSame(rows1, rows2);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var service = CreateService();

        var exception = Record.Exception(() => service.Dispose());

        Assert.Null(exception);
    }

    [Fact]
    public void StartCapture_DoesNotThrow_WhenProxyNotRunning()
    {
        using var service = CreateService();

        var exception = Record.Exception(() => service.StartCapture());

        Assert.Null(exception);
    }

    [Fact]
    public void LoadRows_ReplacesExistingRows()
    {
        using var service = CreateService();

        var rows = new List<InspectionRow>
        {
            new() { Method = "GET", Url = "https://example.com/1", Timestamp = DateTime.UtcNow },
            new() { Method = "POST", Url = "https://example.com/2", Timestamp = DateTime.UtcNow }
        };

        service.LoadRows(rows);

        var loaded = service.GetRows();
        Assert.Equal(2, loaded.Count);
        Assert.Equal("GET", loaded[0].Method);
        Assert.Equal("POST", loaded[1].Method);
    }

    [Fact]
    public void LoadRows_AssignsSequentialIds()
    {
        using var service = CreateService();

        var rows = new List<InspectionRow>
        {
            new() { Method = "GET", Url = "https://example.com/1", Timestamp = DateTime.UtcNow },
            new() { Method = "POST", Url = "https://example.com/2", Timestamp = DateTime.UtcNow }
        };

        service.LoadRows(rows);

        var loaded = service.GetRows();
        Assert.Equal(1, loaded[0].Id);
        Assert.Equal(2, loaded[1].Id);
    }

    [Fact]
    public void LoadRows_ClearsPreviousRows()
    {
        using var service = CreateService();

        service.LoadRows(new List<InspectionRow>
        {
            new() { Method = "GET", Url = "https://example.com/old", Timestamp = DateTime.UtcNow }
        });

        service.LoadRows(new List<InspectionRow>
        {
            new() { Method = "POST", Url = "https://example.com/new", Timestamp = DateTime.UtcNow }
        });

        var loaded = service.GetRows();
        Assert.Single(loaded);
        Assert.Equal("POST", loaded[0].Method);
        Assert.Equal(1, loaded[0].Id);
    }

    [Fact]
    public void LoadRows_FiresOnRowsChanged()
    {
        using var service = CreateService();
        var changed = false;
        service.OnRowsChanged += () => changed = true;

        service.LoadRows(new List<InspectionRow>
        {
            new() { Method = "GET", Url = "https://example.com", Timestamp = DateTime.UtcNow }
        });

        Assert.True(changed);
    }

    [Fact]
    public void LoadRows_SetsOriginToLoaded()
    {
        using var service = CreateService();

        var rows = new List<InspectionRow>
        {
            new() { Method = "GET", Url = "https://example.com", Timestamp = DateTime.UtcNow }
        };

        service.LoadRows(rows);

        var loaded = service.GetRows();
        Assert.All(loaded, r => Assert.Equal(RowOrigin.Loaded, r.Origin));
    }

    [Fact]
    public void NewRows_DefaultToLiveOrigin()
    {
        var row = new InspectionRow { Method = "GET", Url = "https://example.com" };
        Assert.Equal(RowOrigin.Live, row.Origin);
    }

    [Fact]
    public void IsCapturing_ReturnsFalse_Initially()
    {
        using var service = CreateService();
        Assert.False(service.IsCapturing);
    }

    [Fact]
    public void LoadRows_SetsActiveSession()
    {
        using var service = CreateService();

        service.LoadRows(
            new List<InspectionRow> { new() { Method = "GET", Url = "https://example.com", Timestamp = DateTime.UtcNow } },
            "session-123",
            "My Session");

        Assert.Equal("session-123", service.ActiveSessionId);
        Assert.Equal("My Session", service.ActiveSessionName);
    }

    [Fact]
    public void LoadRows_WithoutSessionInfo_ClearsActiveSession()
    {
        using var service = CreateService();

        service.LoadRows(
            new List<InspectionRow> { new() { Method = "GET", Url = "https://example.com", Timestamp = DateTime.UtcNow } },
            "session-123",
            "My Session");

        service.LoadRows(
            new List<InspectionRow> { new() { Method = "POST", Url = "https://example.com", Timestamp = DateTime.UtcNow } });

        Assert.Null(service.ActiveSessionId);
        Assert.Null(service.ActiveSessionName);
    }

    [Fact]
    public void Clear_ClearsActiveSession()
    {
        using var service = CreateService();

        service.LoadRows(
            new List<InspectionRow> { new() { Method = "GET", Url = "https://example.com", Timestamp = DateTime.UtcNow } },
            "session-123",
            "My Session");

        service.Clear();

        Assert.Null(service.ActiveSessionId);
        Assert.Null(service.ActiveSessionName);
    }

    [Fact]
    public void ActiveSession_IsNull_Initially()
    {
        using var service = CreateService();
        Assert.Null(service.ActiveSessionId);
        Assert.Null(service.ActiveSessionName);
    }

    [Fact]
    public void ProcessEvent_PairsResponseToRequest_ByCorrelationId()
    {
        using var service = CreateService();
        var now = DateTime.UtcNow;

        service.ProcessEvent(new InspectionEventDto(now, "request", "GET", "https://example.com/1", null, CorrelationId: "corr-1"));
        service.ProcessEvent(new InspectionEventDto(now.AddMilliseconds(100), "response", "", "", 200, CorrelationId: "corr-1"));

        var rows = service.GetRows();
        Assert.Single(rows);
        Assert.Equal("GET", rows[0].Method);
        Assert.Equal(200, rows[0].StatusCode);
    }

    [Fact]
    public void ProcessEvent_PairsOutOfOrderResponses_Correctly()
    {
        using var service = CreateService();
        var now = DateTime.UtcNow;

        // Send 3 requests
        service.ProcessEvent(new InspectionEventDto(now, "request", "GET", "https://example.com/1", null, CorrelationId: "corr-1"));
        service.ProcessEvent(new InspectionEventDto(now, "request", "POST", "https://example.com/2", null, CorrelationId: "corr-2"));
        service.ProcessEvent(new InspectionEventDto(now, "request", "PUT", "https://example.com/3", null, CorrelationId: "corr-3"));

        // Responses arrive out of order: 3, 1, 2
        service.ProcessEvent(new InspectionEventDto(now.AddMilliseconds(50), "response", "", "", 201, CorrelationId: "corr-3"));
        service.ProcessEvent(new InspectionEventDto(now.AddMilliseconds(100), "response", "", "", 200, CorrelationId: "corr-1"));
        service.ProcessEvent(new InspectionEventDto(now.AddMilliseconds(150), "response", "", "", 202, CorrelationId: "corr-2"));

        var rows = service.GetRows();
        Assert.Equal(3, rows.Count);

        // Each response should be paired with its correct request
        Assert.Equal("GET", rows[0].Method);
        Assert.Equal(200, rows[0].StatusCode);

        Assert.Equal("POST", rows[1].Method);
        Assert.Equal(202, rows[1].StatusCode);

        Assert.Equal("PUT", rows[2].Method);
        Assert.Equal(201, rows[2].StatusCode);
    }

    [Fact]
    public void ProcessEvent_ResponseWithoutCorrelationId_IsIgnored()
    {
        using var service = CreateService();
        var now = DateTime.UtcNow;

        service.ProcessEvent(new InspectionEventDto(now, "request", "GET", "https://example.com", null, CorrelationId: "corr-1"));
        service.ProcessEvent(new InspectionEventDto(now.AddMilliseconds(100), "response", "", "", 500, CorrelationId: null));

        var rows = service.GetRows();
        Assert.Single(rows);
        Assert.Null(rows[0].StatusCode); // Response was not paired
    }

    [Fact]
    public void ProcessEvent_ResponseWithUnknownCorrelationId_IsIgnored()
    {
        using var service = CreateService();
        var now = DateTime.UtcNow;

        service.ProcessEvent(new InspectionEventDto(now, "request", "GET", "https://example.com", null, CorrelationId: "corr-1"));
        service.ProcessEvent(new InspectionEventDto(now.AddMilliseconds(100), "response", "", "", 404, CorrelationId: "corr-unknown"));

        var rows = service.GetRows();
        Assert.Single(rows);
        Assert.Null(rows[0].StatusCode); // Response was not paired
    }

    [Fact]
    public void ProcessEvent_PassthroughEvent_CreatesRowWithPassthroughFlag()
    {
        using var service = CreateService();
        var now = DateTime.UtcNow;

        service.ProcessEvent(new InspectionEventDto(now, "passthrough", "CONNECT", "https://example.com:443", null, CorrelationId: "pt-1"));

        var rows = service.GetRows();
        Assert.Single(rows);
        Assert.True(rows[0].IsPassthrough);
        Assert.Equal("CONNECT", rows[0].Method);
        Assert.Equal("https://example.com:443", rows[0].Url);
    }

    [Fact]
    public void ProcessEvent_PassthroughEvent_DoesNotAffectPendingRequests()
    {
        using var service = CreateService();
        var now = DateTime.UtcNow;

        service.ProcessEvent(new InspectionEventDto(now, "request", "GET", "https://other.com", null, CorrelationId: "corr-1"));
        service.ProcessEvent(new InspectionEventDto(now, "passthrough", "CONNECT", "https://example.com:443", null, CorrelationId: "pt-1"));
        service.ProcessEvent(new InspectionEventDto(now.AddMilliseconds(100), "response", "", "", 200, CorrelationId: "corr-1"));

        var rows = service.GetRows();
        Assert.Equal(2, rows.Count);
        Assert.False(rows[0].IsPassthrough);
        Assert.Equal(200, rows[0].StatusCode);
        Assert.True(rows[1].IsPassthrough);
    }

    [Fact]
    public void FilterState_HasDefaults()
    {
        using var service = CreateService();

        Assert.Equal("", service.SearchQuery);
        Assert.Equal("", service.MethodFilter);
        Assert.Equal("all", service.ProtocolFilter);
        Assert.False(service.ApiOnlyFilter);
        Assert.True(service.AllDomainsSelected);
        Assert.Empty(service.SelectedDomains);
    }

    [Fact]
    public void FilterState_PersistsAcrossAccesses()
    {
        using var service = CreateService();

        service.SearchQuery = "example";
        service.MethodFilter = "GET";
        service.ProtocolFilter = "http";
        service.ApiOnlyFilter = true;
        service.AllDomainsSelected = false;
        service.SelectedDomains.Add("example.com");

        Assert.Equal("example", service.SearchQuery);
        Assert.Equal("GET", service.MethodFilter);
        Assert.Equal("http", service.ProtocolFilter);
        Assert.True(service.ApiOnlyFilter);
        Assert.False(service.AllDomainsSelected);
        Assert.Contains("example.com", service.SelectedDomains);
    }

    [Fact]
    public void ApplicationStopping_StopsCapture()
    {
        var lifetime = new TestHostApplicationLifetime();
        using var service = CreateService(lifetime);

        service.StartCapture();
        Assert.True(service.IsCapturing);

        lifetime.TriggerStopping();

        // Give the background task a moment to observe the cancellation
        Thread.Sleep(100);
        Assert.False(service.IsCapturing);
    }

    [Fact]
    public void ProcessEvent_ImageResponse_StoresBase64()
    {
        using var service = CreateService();
        var now = DateTime.UtcNow;
        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG magic bytes

        service.ProcessEvent(new InspectionEventDto(now, "request", "GET", "https://example.com/logo.png", null, CorrelationId: "corr-img"));
        service.ProcessEvent(new InspectionEventDto(
            now.AddMilliseconds(50), "response", "", "", 200,
            Headers: new List<KeyValuePair<string, string>> { new("Content-Type", "image/png") },
            Body: imageBytes,
            CorrelationId: "corr-img"));

        var rows = service.GetRows();
        Assert.Single(rows);
        Assert.Equal("image/png", rows[0].ResponseContentType);
        Assert.Equal(Convert.ToBase64String(imageBytes), rows[0].ResponseBodyBase64);
        Assert.Contains("Image:", rows[0].ResponseBody);
    }

    [Fact]
    public void ProcessEvent_JpegResponse_StoresBase64()
    {
        using var service = CreateService();
        var now = DateTime.UtcNow;
        var imageBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }; // JPEG magic bytes

        service.ProcessEvent(new InspectionEventDto(now, "request", "GET", "https://example.com/photo.jpg", null, CorrelationId: "corr-jpg"));
        service.ProcessEvent(new InspectionEventDto(
            now.AddMilliseconds(50), "response", "", "", 200,
            Headers: new List<KeyValuePair<string, string>> { new("Content-Type", "image/jpeg; charset=binary") },
            Body: imageBytes,
            CorrelationId: "corr-jpg"));

        var rows = service.GetRows();
        Assert.Single(rows);
        Assert.Equal("image/jpeg", rows[0].ResponseContentType);
        Assert.Equal(Convert.ToBase64String(imageBytes), rows[0].ResponseBodyBase64);
    }

    [Fact]
    public void ProcessEvent_NonImageResponse_DoesNotStoreBase64()
    {
        using var service = CreateService();
        var now = DateTime.UtcNow;
        var jsonBytes = System.Text.Encoding.UTF8.GetBytes("{\"ok\":true}");

        service.ProcessEvent(new InspectionEventDto(now, "request", "GET", "https://example.com/api", null, CorrelationId: "corr-json"));
        service.ProcessEvent(new InspectionEventDto(
            now.AddMilliseconds(50), "response", "", "", 200,
            Headers: new List<KeyValuePair<string, string>> { new("Content-Type", "application/json") },
            Body: jsonBytes,
            CorrelationId: "corr-json"));

        var rows = service.GetRows();
        Assert.Single(rows);
        Assert.Null(rows[0].ResponseBodyBase64);
        Assert.Equal("application/json", rows[0].ResponseContentType);
        Assert.Equal("{\"ok\":true}", rows[0].ResponseBody);
    }

    [Fact]
    public void ProcessEvent_ImageResponse_EmptyBody_DoesNotStoreBase64()
    {
        using var service = CreateService();
        var now = DateTime.UtcNow;

        service.ProcessEvent(new InspectionEventDto(now, "request", "GET", "https://example.com/empty.png", null, CorrelationId: "corr-empty"));
        service.ProcessEvent(new InspectionEventDto(
            now.AddMilliseconds(50), "response", "", "", 200,
            Headers: new List<KeyValuePair<string, string>> { new("Content-Type", "image/png") },
            Body: Array.Empty<byte>(),
            CorrelationId: "corr-empty"));

        var rows = service.GetRows();
        Assert.Single(rows);
        Assert.Null(rows[0].ResponseBodyBase64);
    }

    [Theory]
    [InlineData("Content-Type", "image/png")]
    [InlineData("content-type", "image/png")]
    public void GetContentType_ExtractsMediaType(string headerName, string expected)
    {
        var headers = new List<KeyValuePair<string, string>> { new(headerName, "image/png; charset=binary") };
        Assert.Equal(expected, InspectionDataService.GetContentType(headers));
    }

    [Fact]
    public void GetContentType_ReturnsNull_WhenMissing()
    {
        var headers = new List<KeyValuePair<string, string>>();
        Assert.Null(InspectionDataService.GetContentType(headers));
    }

    [Fact]
    public void ProcessEvent_Request_StoresRequestBody()
    {
        using var service = CreateService();
        var now = DateTime.UtcNow;
        var bodyBytes = System.Text.Encoding.UTF8.GetBytes("{\"name\":\"test\"}");

        service.ProcessEvent(new InspectionEventDto(now, "request", "POST", "https://example.com/api",
            null,
            Headers: new List<KeyValuePair<string, string>> { new("Content-Type", "application/json") },
            Body: bodyBytes,
            CorrelationId: "corr-body"));

        var rows = service.GetRows();
        Assert.Single(rows);
        Assert.Equal("{\"name\":\"test\"}", rows[0].RequestBody);
    }

    [Fact]
    public void ProcessEvent_Request_StoresRequestHeaders()
    {
        using var service = CreateService();
        var now = DateTime.UtcNow;
        var headers = new List<KeyValuePair<string, string>>
        {
            new("Authorization", "Bearer token123"),
            new("Content-Type", "application/json")
        };

        service.ProcessEvent(new InspectionEventDto(now, "request", "GET", "https://example.com/api",
            null, Headers: headers, CorrelationId: "corr-hdr"));

        var rows = service.GetRows();
        Assert.Single(rows);
        Assert.Equal(2, rows[0].RequestHeaders.Count);
        Assert.Contains(rows[0].RequestHeaders, h => h.Key == "Authorization");
    }

    [Fact]
    public void ProcessEvent_Response_CalculatesDuration()
    {
        using var service = CreateService();
        var now = DateTime.UtcNow;

        service.ProcessEvent(new InspectionEventDto(now, "request", "GET", "https://example.com", null, CorrelationId: "corr-dur"));
        service.ProcessEvent(new InspectionEventDto(now.AddMilliseconds(250), "response", "", "", 200, CorrelationId: "corr-dur"));

        var rows = service.GetRows();
        Assert.Single(rows);
        Assert.NotNull(rows[0].Duration);
        Assert.Equal(250, rows[0].Duration!.Value.TotalMilliseconds, precision: 1);
    }

    [Fact]
    public void ProcessEvent_Response_StoresResponseHeaders()
    {
        using var service = CreateService();
        var now = DateTime.UtcNow;
        var responseHeaders = new List<KeyValuePair<string, string>>
        {
            new("Content-Type", "application/json"),
            new("X-Request-Id", "abc-123")
        };

        service.ProcessEvent(new InspectionEventDto(now, "request", "GET", "https://example.com", null, CorrelationId: "corr-rh"));
        service.ProcessEvent(new InspectionEventDto(now.AddMilliseconds(50), "response", "", "", 200,
            Headers: responseHeaders, CorrelationId: "corr-rh"));

        var rows = service.GetRows();
        Assert.Single(rows);
        Assert.Equal(2, rows[0].ResponseHeaders.Count);
        Assert.Contains(rows[0].ResponseHeaders, h => h.Key == "X-Request-Id" && h.Value == "abc-123");
    }

    [Fact]
    public void ProcessEvent_Response_StoresResponseBody()
    {
        using var service = CreateService();
        var now = DateTime.UtcNow;
        var bodyBytes = System.Text.Encoding.UTF8.GetBytes("{\"result\":\"ok\"}");

        service.ProcessEvent(new InspectionEventDto(now, "request", "GET", "https://example.com/api", null, CorrelationId: "corr-rb"));
        service.ProcessEvent(new InspectionEventDto(now.AddMilliseconds(50), "response", "", "", 200,
            Headers: new List<KeyValuePair<string, string>> { new("Content-Type", "application/json") },
            Body: bodyBytes,
            CorrelationId: "corr-rb"));

        var rows = service.GetRows();
        Assert.Single(rows);
        Assert.Equal("{\"result\":\"ok\"}", rows[0].ResponseBody);
    }

    [Fact]
    public void ProcessEvent_Response_NullBody_SetsResponseBodyNull()
    {
        using var service = CreateService();
        var now = DateTime.UtcNow;

        service.ProcessEvent(new InspectionEventDto(now, "request", "GET", "https://example.com", null, CorrelationId: "corr-nb"));
        service.ProcessEvent(new InspectionEventDto(now.AddMilliseconds(50), "response", "", "", 204,
            Body: null, CorrelationId: "corr-nb"));

        var rows = service.GetRows();
        Assert.Single(rows);
        Assert.Null(rows[0].ResponseBody);
    }

    [Fact]
    public void ProcessEvent_Response_EmptyBody_SetsResponseBodyNull()
    {
        using var service = CreateService();
        var now = DateTime.UtcNow;

        service.ProcessEvent(new InspectionEventDto(now, "request", "GET", "https://example.com", null, CorrelationId: "corr-eb"));
        service.ProcessEvent(new InspectionEventDto(now.AddMilliseconds(50), "response", "", "", 204,
            Body: Array.Empty<byte>(), CorrelationId: "corr-eb"));

        var rows = service.GetRows();
        Assert.Single(rows);
        Assert.Null(rows[0].ResponseBody);
    }

    [Fact]
    public void ProcessEvent_WebSocketOpen_CreatesWebSocketRow()
    {
        using var service = CreateService();
        var now = DateTime.UtcNow;
        var headers = new List<KeyValuePair<string, string>>
        {
            new("Upgrade", "websocket"),
            new("Connection", "Upgrade")
        };

        service.ProcessEvent(new InspectionEventDto(now, "websocket_open", "GET", "wss://example.com/ws",
            101, Headers: headers, CorrelationId: "ws-1"));

        var rows = service.GetRows();
        Assert.Single(rows);
        Assert.True(rows[0].IsWebSocket);
        Assert.Equal("GET", rows[0].Method);
        Assert.Equal("wss://example.com/ws", rows[0].Url);
        Assert.Equal(101, rows[0].StatusCode);
        Assert.Equal(2, rows[0].RequestHeaders.Count);
    }

    [Fact]
    public void ProcessEvent_WebSocketMessage_AddsFrame()
    {
        using var service = CreateService();
        var now = DateTime.UtcNow;

        service.ProcessEvent(new InspectionEventDto(now, "websocket_open", "GET", "wss://example.com/ws",
            101, CorrelationId: "ws-2"));

        var msgBody = System.Text.Encoding.UTF8.GetBytes("hello");
        service.ProcessEvent(new InspectionEventDto(now.AddMilliseconds(100), "websocket_message", "", "",
            null, Body: msgBody, CorrelationId: "ws-2", FrameType: "text", Direction: "send"));

        var rows = service.GetRows();
        Assert.Single(rows);
        Assert.Single(rows[0].WebSocketFrames);
        Assert.Equal("text", rows[0].WebSocketFrames[0].FrameType);
        Assert.Equal("send", rows[0].WebSocketFrames[0].Direction);
        Assert.Equal("hello", rows[0].WebSocketFrames[0].Payload);
    }

    [Fact]
    public void ProcessEvent_WebSocketClose_SetsClosedAndDuration()
    {
        using var service = CreateService();
        var now = DateTime.UtcNow;

        service.ProcessEvent(new InspectionEventDto(now, "websocket_open", "GET", "wss://example.com/ws",
            101, CorrelationId: "ws-3"));
        service.ProcessEvent(new InspectionEventDto(now.AddSeconds(5), "websocket_close", "", "",
            null, CorrelationId: "ws-3"));

        var rows = service.GetRows();
        Assert.Single(rows);
        Assert.True(rows[0].WebSocketClosed);
        Assert.NotNull(rows[0].Duration);
        Assert.Equal(5, rows[0].Duration!.Value.TotalSeconds, precision: 0);
    }

    [Fact]
    public void ProcessEvent_MaxRows_RemovesOldestRow()
    {
        using var service = CreateService();
        var now = DateTime.UtcNow;

        // Fill to MaxRows + 1
        for (var i = 0; i <= InspectionDataService.MaxRows; i++)
        {
            service.ProcessEvent(new InspectionEventDto(
                now.AddMilliseconds(i), "request", "GET", $"https://example.com/{i}",
                null, CorrelationId: $"corr-{i}"));
        }

        var rows = service.GetRows();
        Assert.Equal(InspectionDataService.MaxRows, rows.Count);
        // The first URL should have been evicted
        Assert.DoesNotContain(rows, r => r.Url == "https://example.com/0");
        // The last URL should still be present
        Assert.Contains(rows, r => r.Url == $"https://example.com/{InspectionDataService.MaxRows}");
    }

    [Fact]
    public void ProcessEvent_MaxRows_ResponseStillPairsAfterEviction()
    {
        using var service = CreateService();
        var now = DateTime.UtcNow;

        // Add a request that will survive eviction
        service.ProcessEvent(new InspectionEventDto(
            now, "request", "GET", "https://example.com/survivor",
            null, CorrelationId: "corr-survivor"));

        // Fill remaining slots to trigger eviction of other rows
        for (var i = 1; i <= InspectionDataService.MaxRows; i++)
        {
            service.ProcessEvent(new InspectionEventDto(
                now.AddMilliseconds(i), "request", "GET", $"https://example.com/filler-{i}",
                null, CorrelationId: $"corr-filler-{i}"));
        }

        // The survivor should have been evicted (it was index 0)
        var rows = service.GetRows();
        Assert.DoesNotContain(rows, r => r.Url == "https://example.com/survivor");

        // A response for the evicted request should not crash
        service.ProcessEvent(new InspectionEventDto(
            now.AddSeconds(1), "response", "", "", 200, CorrelationId: "corr-survivor"));

        // Should still have MaxRows (no crash, orphan response ignored)
        rows = service.GetRows();
        Assert.Equal(InspectionDataService.MaxRows, rows.Count);
    }

    [Fact]
    public void ProcessEvent_Request_NullHeaders_DefaultsToEmptyList()
    {
        using var service = CreateService();
        var now = DateTime.UtcNow;

        service.ProcessEvent(new InspectionEventDto(now, "request", "GET", "https://example.com",
            null, Headers: null, CorrelationId: "corr-nh"));

        var rows = service.GetRows();
        Assert.Single(rows);
        Assert.NotNull(rows[0].RequestHeaders);
        Assert.Empty(rows[0].RequestHeaders);
    }

    [Fact]
    public void ProcessEvent_Response_NullHeaders_DefaultsToEmptyList()
    {
        using var service = CreateService();
        var now = DateTime.UtcNow;

        service.ProcessEvent(new InspectionEventDto(now, "request", "GET", "https://example.com", null, CorrelationId: "corr-rnh"));
        service.ProcessEvent(new InspectionEventDto(now.AddMilliseconds(50), "response", "", "", 200,
            Headers: null, CorrelationId: "corr-rnh"));

        var rows = service.GetRows();
        Assert.Single(rows);
        Assert.NotNull(rows[0].ResponseHeaders);
        Assert.Empty(rows[0].ResponseHeaders);
    }

    [Fact]
    public void ProcessEvent_Request_NullBody_SetsRequestBodyNull()
    {
        using var service = CreateService();
        var now = DateTime.UtcNow;

        service.ProcessEvent(new InspectionEventDto(now, "request", "GET", "https://example.com",
            null, Body: null, CorrelationId: "corr-rnb"));

        var rows = service.GetRows();
        Assert.Single(rows);
        Assert.Null(rows[0].RequestBody);
    }

    [Fact]
    public void ProcessEvent_Response_StoresContentType()
    {
        using var service = CreateService();
        var now = DateTime.UtcNow;

        service.ProcessEvent(new InspectionEventDto(now, "request", "GET", "https://example.com", null, CorrelationId: "corr-ct"));
        service.ProcessEvent(new InspectionEventDto(now.AddMilliseconds(50), "response", "", "", 200,
            Headers: new List<KeyValuePair<string, string>> { new("Content-Type", "text/html; charset=utf-8") },
            CorrelationId: "corr-ct"));

        var rows = service.GetRows();
        Assert.Single(rows);
        Assert.Equal("text/html", rows[0].ResponseContentType);
    }

    [Fact]
    public void ProcessEvent_UnknownEventType_IsIgnored()
    {
        using var service = CreateService();
        var now = DateTime.UtcNow;

        service.ProcessEvent(new InspectionEventDto(now, "unknown_event", "GET", "https://example.com",
            null, CorrelationId: "corr-unk"));

        var rows = service.GetRows();
        Assert.Empty(rows);
    }

    [Fact]
    public void ProcessEvent_CaseInsensitive_EventTypes()
    {
        using var service = CreateService();
        var now = DateTime.UtcNow;

        service.ProcessEvent(new InspectionEventDto(now, "REQUEST", "GET", "https://example.com/upper",
            null, CorrelationId: "corr-case1"));
        service.ProcessEvent(new InspectionEventDto(now.AddMilliseconds(50), "Response", "", "", 200,
            CorrelationId: "corr-case1"));

        var rows = service.GetRows();
        Assert.Single(rows);
        Assert.Equal(200, rows[0].StatusCode);
    }

    [Fact]
    public void ProcessEvent_DuplicateResponse_SecondIsIgnored()
    {
        using var service = CreateService();
        var now = DateTime.UtcNow;

        service.ProcessEvent(new InspectionEventDto(now, "request", "GET", "https://example.com", null, CorrelationId: "corr-dup"));
        service.ProcessEvent(new InspectionEventDto(now.AddMilliseconds(50), "response", "", "", 200, CorrelationId: "corr-dup"));
        // Second response for same correlation ID should be ignored (already removed from pending)
        service.ProcessEvent(new InspectionEventDto(now.AddMilliseconds(100), "response", "", "", 500, CorrelationId: "corr-dup"));

        var rows = service.GetRows();
        Assert.Single(rows);
        Assert.Equal(200, rows[0].StatusCode); // First response wins
    }

    [Fact]
    public void ProcessEvent_WebSocketMessage_WithoutOpen_IsIgnored()
    {
        using var service = CreateService();
        var now = DateTime.UtcNow;

        // Send a websocket_message without a preceding websocket_open
        var body = System.Text.Encoding.UTF8.GetBytes("orphan message");
        service.ProcessEvent(new InspectionEventDto(now, "websocket_message", "", "", null,
            Body: body, CorrelationId: "ws-orphan", FrameType: "text", Direction: "send"));

        var rows = service.GetRows();
        Assert.Empty(rows);
    }

    [Fact]
    public void ProcessEvent_MultipleWebSocketFrames_AccumulateInOrder()
    {
        using var service = CreateService();
        var now = DateTime.UtcNow;

        service.ProcessEvent(new InspectionEventDto(now, "websocket_open", "GET", "wss://example.com/ws",
            101, CorrelationId: "ws-multi"));

        for (var i = 0; i < 5; i++)
        {
            var body = System.Text.Encoding.UTF8.GetBytes($"msg-{i}");
            service.ProcessEvent(new InspectionEventDto(
                now.AddMilliseconds(100 * (i + 1)), "websocket_message", "", "", null,
                Body: body, CorrelationId: "ws-multi", FrameType: "text", Direction: i % 2 == 0 ? "send" : "receive"));
        }

        var rows = service.GetRows();
        Assert.Single(rows);
        Assert.Equal(5, rows[0].WebSocketFrames.Count);
        Assert.Equal("msg-0", rows[0].WebSocketFrames[0].Payload);
        Assert.Equal("msg-4", rows[0].WebSocketFrames[4].Payload);
        Assert.Equal("send", rows[0].WebSocketFrames[0].Direction);
        Assert.Equal("receive", rows[0].WebSocketFrames[1].Direction);
    }

    [Fact]
    public void ProcessEvent_Passthrough_MaxRows_RemovesOldest()
    {
        using var service = CreateService();
        var now = DateTime.UtcNow;

        // Fill to MaxRows with passthrough events
        for (var i = 0; i <= InspectionDataService.MaxRows; i++)
        {
            service.ProcessEvent(new InspectionEventDto(
                now.AddMilliseconds(i), "passthrough", "CONNECT", $"https://example.com:{i}",
                null, CorrelationId: $"pt-{i}"));
        }

        var rows = service.GetRows();
        Assert.Equal(InspectionDataService.MaxRows, rows.Count);
        Assert.DoesNotContain(rows, r => r.Url == "https://example.com:0");
    }

    [Fact]
    public void StopCapture_SetsIsCapturingFalse()
    {
        using var service = CreateService();

        service.StopCapture();

        Assert.False(service.IsCapturing);
    }

    [Fact]
    public void Reconnect_WhenDisconnected_StartsCapture()
    {
        using var service = CreateService();

        // Reconnect when disconnected should not throw
        var exception = Record.Exception(() => service.Reconnect());
        Assert.Null(exception);
    }

    // NOTE: ResponseBodySize tests will be added when #234 (response-size-column) is merged.
    // Tests prepared:
    // - ProcessEvent_Response_SetsResponseBodySize
    // - ProcessEvent_Response_NullBody_ResponseBodySizeIsNull
    // - ProcessEvent_Response_EmptyBody_ResponseBodySizeIsZero
    // - ProcessEvent_ImageResponse_SetsResponseBodySize

    [Fact]
    public void GetContentType_ExtractsMediaType_WithParams()
    {
        var headers = new List<KeyValuePair<string, string>> { new("Content-Type", "text/html; charset=utf-8; boundary=something") };
        Assert.Equal("text/html", InspectionDataService.GetContentType(headers));
    }

    [Fact]
    public void GetContentType_NormalizesToLowercase()
    {
        var headers = new List<KeyValuePair<string, string>> { new("Content-Type", "Application/JSON") };
        Assert.Equal("application/json", InspectionDataService.GetContentType(headers));
    }

    [Fact]
    public void ProcessEvent_Response_StoresTiming()
    {
        using var service = CreateService();
        var now = DateTime.UtcNow;
        var timing = new TimingInfo
        {
            ConnectMs = 12.5,
            TlsMs = 30.0,
            SendMs = 1.1,
            WaitMs = 45.3,
            ReceiveMs = 8.9,
            Reused = false
        };

        service.ProcessEvent(new InspectionEventDto(now, "request", "GET", "https://example.com", null, CorrelationId: "corr-timing"));
        service.ProcessEvent(new InspectionEventDto(now.AddMilliseconds(100), "response", "", "", 200, CorrelationId: "corr-timing", Timing: timing));

        var rows = service.GetRows();
        Assert.Single(rows);
        Assert.NotNull(rows[0].Timing);
        Assert.Equal(12.5, rows[0].Timing!.ConnectMs);
        Assert.Equal(30.0, rows[0].Timing!.TlsMs);
        Assert.Equal(1.1, rows[0].Timing!.SendMs);
        Assert.Equal(45.3, rows[0].Timing!.WaitMs);
        Assert.Equal(8.9, rows[0].Timing!.ReceiveMs);
        Assert.False(rows[0].Timing!.Reused);
    }

    [Fact]
    public void ProcessEvent_Response_NullTiming_LeavesTimingNull()
    {
        using var service = CreateService();
        var now = DateTime.UtcNow;

        service.ProcessEvent(new InspectionEventDto(now, "request", "GET", "https://example.com", null, CorrelationId: "corr-notiming"));
        service.ProcessEvent(new InspectionEventDto(now.AddMilliseconds(50), "response", "", "", 200, CorrelationId: "corr-notiming"));

        var rows = service.GetRows();
        Assert.Single(rows);
        Assert.Null(rows[0].Timing);
    }

    [Fact]
    public void ProcessEvent_Response_ReusedConnectionTiming()
    {
        using var service = CreateService();
        var now = DateTime.UtcNow;
        var timing = new TimingInfo
        {
            ConnectMs = null,
            TlsMs = null,
            SendMs = 0.5,
            WaitMs = 20.0,
            ReceiveMs = 5.0,
            Reused = true
        };

        service.ProcessEvent(new InspectionEventDto(now, "request", "GET", "https://example.com", null, CorrelationId: "corr-reused"));
        service.ProcessEvent(new InspectionEventDto(now.AddMilliseconds(50), "response", "", "", 200, CorrelationId: "corr-reused", Timing: timing));

        var rows = service.GetRows();
        Assert.Single(rows);
        Assert.NotNull(rows[0].Timing);
        Assert.Null(rows[0].Timing!.ConnectMs);
        Assert.Null(rows[0].Timing!.TlsMs);
        Assert.True(rows[0].Timing!.Reused);
    }

    [Fact]
    public void InspectionRow_Timing_DefaultsToNull()
    {
        var row = new InspectionRow { Method = "GET", Url = "https://example.com" };
        Assert.Null(row.Timing);
    }

    private class TestHostApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _stoppingCts = new();
        private readonly CancellationTokenSource _startedCts = new();
        private readonly CancellationTokenSource _stoppedCts = new();

        public CancellationToken ApplicationStarted => _startedCts.Token;
        public CancellationToken ApplicationStopping => _stoppingCts.Token;
        public CancellationToken ApplicationStopped => _stoppedCts.Token;

        public void StopApplication() => _stoppingCts.Cancel();
        public void TriggerStopping() => _stoppingCts.Cancel();
    }
}
