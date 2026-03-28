using shmoxy.frontend.services;
using Xunit;

namespace shmoxy.frontend.tests.services;

public class ProxyStatusServiceTests
{
    private static ProxyStatusService CreateService()
    {
        var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost") };
        var apiClient = new ApiClient(httpClient);
        return new ProxyStatusService(apiClient);
    }

    [Fact]
    public void CurrentStatus_IsStoppedByDefault()
    {
        using var service = CreateService();

        Assert.False(service.CurrentStatus.IsRunning);
    }

    [Fact]
    public void UpdateStatus_ChangesCurrentStatus()
    {
        using var service = CreateService();

        var newStatus = new FrontendProxyStatus(IsRunning: true, Address: "localhost:8080");
        service.UpdateStatus(newStatus);

        Assert.True(service.CurrentStatus.IsRunning);
        Assert.Equal("localhost:8080", service.CurrentStatus.Address);
    }

    [Fact]
    public void UpdateStatus_IncludesProxyVersion()
    {
        using var service = CreateService();

        var newStatus = new FrontendProxyStatus(IsRunning: true, Address: "localhost:8080", ProxyVersion: "1.2.3");
        service.UpdateStatus(newStatus);

        Assert.Equal("1.2.3", service.CurrentStatus.ProxyVersion);
    }

    [Fact]
    public void StoppedStatus_HasNullProxyVersion()
    {
        Assert.Null(FrontendProxyStatus.Stopped.ProxyVersion);
    }

    [Fact]
    public void UpdateStatus_FiresOnStatusChanged()
    {
        using var service = CreateService();
        var changed = false;
        service.OnStatusChanged += () => changed = true;

        service.UpdateStatus(new FrontendProxyStatus(IsRunning: true));

        Assert.True(changed);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var service = CreateService();

        var exception = Record.Exception(() => service.Dispose());

        Assert.Null(exception);
    }

    [Fact]
    public void StartPolling_DoesNotThrow()
    {
        using var service = CreateService();

        var exception = Record.Exception(() => service.StartPolling());

        Assert.Null(exception);
    }
}
