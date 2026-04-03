using shmoxy.ipc;
using shmoxy.server;
using shmoxy.server.hooks;
using shmoxy.shared.ipc;

namespace shmoxy.tests.ipc;

public class ProxyStateServiceTests : IClassFixture<ProxyTestFixture>
{
    private readonly ProxyTestFixture _fixture;

    public ProxyStateServiceTests(ProxyTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Constructor_ThrowsOnNullProxy()
    {
        Assert.Throws<ArgumentNullException>(() => new ProxyStateService(null!));
    }

    [Fact]
    public void Constructor_AcceptsNullOptionalParameters()
    {
        var service = new ProxyStateService(_fixture.Server);

        Assert.Null(service.InspectionHook);
        Assert.Null(service.BreakpointHook);
        Assert.Null(service.SessionLogBuffer);
    }

    [Fact]
    public void IsListening_ReflectsProxyServerState()
    {
        var service = new ProxyStateService(_fixture.Server);

        Assert.True(service.IsListening);
    }

    [Fact]
    public void Port_ReflectsProxyServerPort()
    {
        var service = new ProxyStateService(_fixture.Server);

        Assert.Equal(_fixture.Server.ListeningPort, service.Port);
    }

    [Fact]
    public void Uptime_IsPositive()
    {
        var service = new ProxyStateService(_fixture.Server);

        Assert.True(service.Uptime > TimeSpan.Zero);
    }

    [Fact]
    public void ActiveConnections_ReturnsZero()
    {
        var service = new ProxyStateService(_fixture.Server);

        Assert.Equal(0, service.ActiveConnections);
    }

    [Fact]
    public void EnableInspection_ReturnsFalse_WhenNoInspectionHook()
    {
        var service = new ProxyStateService(_fixture.Server);

        Assert.False(service.EnableInspection());
    }

    [Fact]
    public void DisableInspection_ReturnsFalse_WhenNoInspectionHook()
    {
        var service = new ProxyStateService(_fixture.Server);

        Assert.False(service.DisableInspection());
    }

    [Fact]
    public void EnableInspection_ReturnsTrue_AndEnablesHook()
    {
        var hook = new InspectionHook();
        hook.Enabled = false;
        var service = new ProxyStateService(_fixture.Server, inspectionHook: hook);

        var result = service.EnableInspection();

        Assert.True(result);
        Assert.True(hook.Enabled);
    }

    [Fact]
    public void DisableInspection_ReturnsTrue_AndDisablesHook()
    {
        var hook = new InspectionHook();
        hook.Enabled = true;
        var service = new ProxyStateService(_fixture.Server, inspectionHook: hook);

        var result = service.DisableInspection();

        Assert.True(result);
        Assert.False(hook.Enabled);
    }

    [Fact]
    public void InspectionHook_ExposesPassedHook()
    {
        var hook = new InspectionHook();
        var service = new ProxyStateService(_fixture.Server, inspectionHook: hook);

        Assert.Same(hook, service.InspectionHook);
    }

    [Fact]
    public void BreakpointHook_ExposesPassedHook()
    {
        var hook = new BreakpointHook();
        var service = new ProxyStateService(_fixture.Server, breakpointHook: hook);

        Assert.Same(hook, service.BreakpointHook);
    }

    [Fact]
    public void GetRootCertificatePem_DelegatesToProxy()
    {
        var service = new ProxyStateService(_fixture.Server);

        var pem = service.GetRootCertificatePem();

        Assert.Contains("-----BEGIN CERTIFICATE-----", pem);
    }

    [Fact]
    public void GetRootCertificateDer_DelegatesToProxy()
    {
        var service = new ProxyStateService(_fixture.Server);

        var der = service.GetRootCertificateDer();

        Assert.True(der.Length > 0);
    }

    [Fact]
    public void GetRootCertificatePfx_DelegatesToProxy()
    {
        var service = new ProxyStateService(_fixture.Server);

        // PFX export may fail on macOS due to keychain restrictions on private key export.
        // On other platforms, verify the export returns data.
        if (OperatingSystem.IsMacOS())
        {
            Assert.ThrowsAny<Exception>(() => service.GetRootCertificatePfx());
        }
        else
        {
            var pfx = service.GetRootCertificatePfx();
            Assert.True(pfx.Length > 0);
        }
    }
}
