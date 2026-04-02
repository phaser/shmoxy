using shmoxy.api.ipc;
using shmoxy.api.models;
using shmoxy.api.server;
using shmoxy.shared.ipc;

namespace shmoxy.frontend.tests;

/// <summary>
/// Test double for IProxyProcessManager that simulates proxy lifecycle
/// without spawning a real process. Used by FrontendTestFixture so
/// Playwright tests can click Start/Stop without needing the shmoxy binary.
/// </summary>
public class FakeProxyProcessManager : IProxyProcessManager
{
    private ProxyInstanceState _state = new()
    {
        State = ProxyProcessState.Stopped
    };

    private readonly FakeProxyIpcClient _ipcClient = new();

    public event EventHandler<ProxyInstanceState>? OnStateChanged;

    public Task<ProxyInstanceState> StartAsync(CancellationToken ct = default)
    {
        _state = new ProxyInstanceState
        {
            State = ProxyProcessState.Running,
            Port = 18080,
            StartedAt = DateTime.UtcNow,
            ProxyVersion = "0.0.0-test"
        };
        OnStateChanged?.Invoke(this, _state);
        return Task.FromResult(_state);
    }

    public Task StopAsync(ShutdownSource source = ShutdownSource.User, CancellationToken ct = default)
    {
        _state = new ProxyInstanceState
        {
            State = ProxyProcessState.Stopped,
            StoppedAt = DateTime.UtcNow
        };
        OnStateChanged?.Invoke(this, _state);
        return Task.CompletedTask;
    }

    public Task<ProxyInstanceState> RestartAsync(int? portOverride = null, CancellationToken ct = default)
    {
        _state = new ProxyInstanceState
        {
            State = ProxyProcessState.Running,
            Port = portOverride ?? 18080,
            StartedAt = DateTime.UtcNow,
            ProxyVersion = "0.0.0-test"
        };
        OnStateChanged?.Invoke(this, _state);
        return Task.FromResult(_state);
    }

    public Task<ProxyInstanceState?> GetStateAsync() => Task.FromResult<ProxyInstanceState?>(_state);

    public Task<bool> IsRunningAsync() => Task.FromResult(_state.State == ProxyProcessState.Running);

    public Task<string> GetRootCertPemAsync(CancellationToken ct = default)
        => Task.FromResult("-----BEGIN CERTIFICATE-----\nTESTCERT\n-----END CERTIFICATE-----");

    public Task<byte[]> GetRootCertDerAsync(CancellationToken ct = default)
        => Task.FromResult(new byte[] { 0x30, 0x82, 0x01, 0x00 });

    public IProxyIpcClient GetIpcClient() => _ipcClient;
}

/// <summary>
/// Minimal IPC client stub that returns sensible defaults for API calls
/// made while the fake proxy is "running".
/// </summary>
public class FakeProxyIpcClient : IProxyIpcClient
{
    public Task<ProxyStatus> GetStatusAsync(CancellationToken ct = default)
        => Task.FromResult(new ProxyStatus { IsListening = true, Port = 18080, ActiveConnections = 0, Uptime = TimeSpan.FromSeconds(1) });

    public Task<ShutdownResponse> ShutdownAsync(CancellationToken ct = default)
        => Task.FromResult(new ShutdownResponse { Success = true, Message = "Shutdown initiated" });

    public Task<ProxyConfig> GetConfigAsync(CancellationToken ct = default)
        => Task.FromResult(new ProxyConfig { Port = 18080, LogLevel = ProxyConfig.LogLevelEnum.Info });

    public Task<ProxyConfig> UpdateConfigAsync(ProxyConfig config, CancellationToken ct = default)
        => Task.FromResult(config);

    public Task<IReadOnlyList<HookDescriptor>> GetHooksAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<HookDescriptor>>(Array.Empty<HookDescriptor>());

    public Task<EnableHookResponse> EnableHookAsync(string id, CancellationToken ct = default)
        => Task.FromResult(new EnableHookResponse { Success = true });

    public Task<DisableHookResponse> DisableHookAsync(string id, CancellationToken ct = default)
        => Task.FromResult(new DisableHookResponse { Success = true });

    public Task<EnableInspectionResponse> EnableInspectionAsync(CancellationToken ct = default)
        => Task.FromResult(new EnableInspectionResponse { Success = true });

    public Task<DisableInspectionResponse> DisableInspectionAsync(CancellationToken ct = default)
        => Task.FromResult(new DisableInspectionResponse { Success = true });

    public async IAsyncEnumerable<InspectionEvent> GetInspectionStreamAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    public Task<string> GetRootCertPemAsync(CancellationToken ct = default)
        => Task.FromResult("-----BEGIN CERTIFICATE-----\nTESTCERT\n-----END CERTIFICATE-----");

    public Task<byte[]> GetRootCertDerAsync(CancellationToken ct = default)
        => Task.FromResult(new byte[] { 0x30, 0x82, 0x01, 0x00 });

    public Task<byte[]> GetRootCertPfxAsync(CancellationToken ct = default)
        => Task.FromResult(new byte[] { 0x30, 0x82, 0x01, 0x00 });

    public Task<bool> IsHealthyAsync(CancellationToken ct = default)
        => Task.FromResult(true);

    public Task<IReadOnlyList<SessionLogEntry>> DrainSessionLogAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SessionLogEntry>>(Array.Empty<SessionLogEntry>());

    public Task EnableBreakpointsAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task DisableBreakpointsAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<string> GetPausedRequestsAsync(CancellationToken ct = default)
        => Task.FromResult("[]");

    public Task ReleaseRequestAsync(string correlationId, string? modifiedBody = null, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task DropRequestAsync(string correlationId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<string> GetBreakpointRulesAsync(CancellationToken ct = default)
        => Task.FromResult("[]");

    public Task<string> AddBreakpointRuleAsync(string? method, string urlPattern, CancellationToken ct = default)
        => Task.FromResult("{}");

    public Task RemoveBreakpointRuleAsync(string id, CancellationToken ct = default)
        => Task.CompletedTask;
}
