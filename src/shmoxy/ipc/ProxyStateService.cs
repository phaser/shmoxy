using shmoxy.server;
using shmoxy.server.hooks;

namespace shmoxy.ipc;

/// <summary>
/// Singleton service that exposes proxy state for the IPC API.
/// </summary>
public class ProxyStateService
{
    private readonly ProxyServer _proxy;
    private readonly InspectionHook? _inspectionHook;
    private readonly DateTime _startTime;

    public ProxyStateService(ProxyServer proxy, InspectionHook? inspectionHook = null)
    {
        _proxy = proxy ?? throw new ArgumentNullException(nameof(proxy));
        _inspectionHook = inspectionHook;
        _startTime = DateTime.UtcNow;
    }

    public bool IsListening => _proxy.IsListening;
    public int Port => _proxy.ListeningPort;
    public TimeSpan Uptime => DateTime.UtcNow - _startTime;
    public int ActiveConnections => 0; // TODO: track in ProxyServer

    public InspectionHook? InspectionHook => _inspectionHook;

    public bool EnableInspection()
    {
        if (_inspectionHook == null)
            return false;

        _inspectionHook.Enabled = true;
        return true;
    }

    public bool DisableInspection()
    {
        if (_inspectionHook == null)
            return false;

        _inspectionHook.Enabled = false;
        return true;
    }

    public string GetRootCertificatePem() => _proxy.GetRootCertificatePem();
    public byte[] GetRootCertificateDer() => _proxy.GetRootCertificateDer();
    public byte[] GetRootCertificatePfx() => _proxy.GetRootCertificatePfx();
}
