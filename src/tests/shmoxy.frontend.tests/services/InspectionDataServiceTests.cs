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
}
