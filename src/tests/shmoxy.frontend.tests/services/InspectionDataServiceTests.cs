using shmoxy.frontend.services;
using Xunit;

namespace shmoxy.frontend.tests.services;

public class InspectionDataServiceTests
{
    private static InspectionDataService CreateService()
    {
        var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost") };
        var apiClient = new ApiClient(httpClient);
        return new InspectionDataService(apiClient);
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
}
