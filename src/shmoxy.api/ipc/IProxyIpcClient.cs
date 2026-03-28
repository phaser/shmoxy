using shmoxy.shared.ipc;

namespace shmoxy.api.ipc;

/// <summary>
/// Client for communicating with the proxy process via IPC.
/// </summary>
public interface IProxyIpcClient
{
    Task<ProxyStatus> GetStatusAsync(CancellationToken ct = default);
    Task<ShutdownResponse> ShutdownAsync(CancellationToken ct = default);
    Task<ProxyConfig> GetConfigAsync(CancellationToken ct = default);
    Task<ProxyConfig> UpdateConfigAsync(ProxyConfig config, CancellationToken ct = default);
    Task<IReadOnlyList<HookDescriptor>> GetHooksAsync(CancellationToken ct = default);
    Task<EnableHookResponse> EnableHookAsync(string id, CancellationToken ct = default);
    Task<DisableHookResponse> DisableHookAsync(string id, CancellationToken ct = default);
    Task<EnableInspectionResponse> EnableInspectionAsync(CancellationToken ct = default);
    Task<DisableInspectionResponse> DisableInspectionAsync(CancellationToken ct = default);
    IAsyncEnumerable<InspectionEvent> GetInspectionStreamAsync(CancellationToken ct = default);
    Task<string> GetRootCertPemAsync(CancellationToken ct = default);
    Task<byte[]> GetRootCertDerAsync(CancellationToken ct = default);
    Task<byte[]> GetRootCertPfxAsync(CancellationToken ct = default);
    Task<bool> IsHealthyAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DetectorDescriptor>> GetDetectorsAsync(CancellationToken ct = default);
    Task EnableDetectorAsync(string id, CancellationToken ct = default);
    Task DisableDetectorAsync(string id, CancellationToken ct = default);
    Task AcceptSuggestionAsync(string host, CancellationToken ct = default);
    Task DismissSuggestionAsync(string host, CancellationToken ct = default);
    IAsyncEnumerable<PassthroughSuggestion> GetSuggestionStreamAsync(CancellationToken ct = default);
}
