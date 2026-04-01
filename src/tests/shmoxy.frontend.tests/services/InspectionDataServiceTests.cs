using Microsoft.Extensions.Hosting;
using shmoxy.frontend.models;
using shmoxy.frontend.services;
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
