using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using shmoxy.api.models.configuration;
using shmoxy.shared.ipc;

namespace shmoxy.api.ipc;

/// <summary>
/// HTTP client for communicating with the proxy process via IPC.
/// Supports both Unix Domain Sockets (local) and HTTP with API key (remote).
/// </summary>
public class ProxyIpcClient : IProxyIpcClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ProxyIpcClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _disposed;

    private const int MaxRetries = 3;
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(5);

    public ProxyIpcClient(HttpClient httpClient, ILogger<ProxyIpcClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    public async Task<ProxyStatus> GetStatusAsync(CancellationToken ct = default)
    {
        return await RetryAsync(async () =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(IpcTimeouts.Small);
            return await _httpClient.GetFromJsonAsync<ProxyStatus>("/ipc/status", _jsonOptions, cts.Token)
                ?? throw new InvalidOperationException("Failed to deserialize status");
        }, ct);
    }

    public async Task<ShutdownResponse> ShutdownAsync(CancellationToken ct = default)
    {
        return await RetryAsync(async () =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(IpcTimeouts.Medium);
            var response = await _httpClient.PostAsync("/ipc/shutdown", null, cts.Token);
            return await response.Content.ReadFromJsonAsync<ShutdownResponse>(_jsonOptions, cts.Token)
                ?? throw new InvalidOperationException("Failed to deserialize shutdown response");
        }, ct);
    }

    public async Task<ProxyConfig> GetConfigAsync(CancellationToken ct = default)
    {
        return await RetryAsync(async () =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(IpcTimeouts.Medium);
            return await _httpClient.GetFromJsonAsync<ProxyConfig>("/ipc/config", _jsonOptions, cts.Token)
                ?? throw new InvalidOperationException("Failed to deserialize config");
        }, ct);
    }

    public async Task<ProxyConfig> UpdateConfigAsync(ProxyConfig config, CancellationToken ct = default)
    {
        return await RetryAsync(async () =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(IpcTimeouts.Medium);
            var response = await _httpClient.PutAsJsonAsync("/ipc/config", config, _jsonOptions, cts.Token);
            return await response.Content.ReadFromJsonAsync<ProxyConfig>(_jsonOptions, cts.Token)
                ?? throw new InvalidOperationException("Failed to deserialize updated config");
        }, ct);
    }

    public async Task<IReadOnlyList<HookDescriptor>> GetHooksAsync(CancellationToken ct = default)
    {
        return await RetryAsync(async () =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(IpcTimeouts.Long);
            return await _httpClient.GetFromJsonAsync<IReadOnlyList<HookDescriptor>>("/ipc/hooks", _jsonOptions, cts.Token)
                ?? Array.Empty<HookDescriptor>();
        }, ct);
    }

    public async Task<EnableHookResponse> EnableHookAsync(string id, CancellationToken ct = default)
    {
        return await RetryAsync(async () =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(IpcTimeouts.Long);
            var response = await _httpClient.PostAsync($"/ipc/hooks/{id}/enable", null, cts.Token);
            return await response.Content.ReadFromJsonAsync<EnableHookResponse>(_jsonOptions, cts.Token)
                ?? throw new InvalidOperationException("Failed to deserialize enable hook response");
        }, ct);
    }

    public async Task<DisableHookResponse> DisableHookAsync(string id, CancellationToken ct = default)
    {
        return await RetryAsync(async () =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(IpcTimeouts.Long);
            var response = await _httpClient.PostAsync($"/ipc/hooks/{id}/disable", null, cts.Token);
            return await response.Content.ReadFromJsonAsync<DisableHookResponse>(_jsonOptions, cts.Token)
                ?? throw new InvalidOperationException("Failed to deserialize disable hook response");
        }, ct);
    }

    public async Task<EnableInspectionResponse> EnableInspectionAsync(CancellationToken ct = default)
    {
        return await RetryAsync(async () =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(IpcTimeouts.Long);
            var response = await _httpClient.PostAsync("/ipc/inspect/enable", null, cts.Token);
            return await response.Content.ReadFromJsonAsync<EnableInspectionResponse>(_jsonOptions, cts.Token)
                ?? throw new InvalidOperationException("Failed to deserialize enable inspection response");
        }, ct);
    }

    public async Task<DisableInspectionResponse> DisableInspectionAsync(CancellationToken ct = default)
    {
        return await RetryAsync(async () =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(IpcTimeouts.Long);
            var response = await _httpClient.PostAsync("/ipc/inspect/disable", null, cts.Token);
            return await response.Content.ReadFromJsonAsync<DisableInspectionResponse>(_jsonOptions, cts.Token)
                ?? throw new InvalidOperationException("Failed to deserialize disable inspection response");
        }, ct);
    }

    public async IAsyncEnumerable<InspectionEvent> GetInspectionStreamAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        using var stream = await _httpClient.GetStreamAsync("/ipc/inspect/stream", ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line == null) break;

            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                var json = line["data: ".Length..];
                var evt = JsonSerializer.Deserialize<InspectionEvent>(json, _jsonOptions);
                if (evt != null)
                {
                    yield return evt;
                }
            }
        }
    }

    public async Task<string> GetRootCertPemAsync(CancellationToken ct = default)
    {
        return await RetryAsync(async () =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(IpcTimeouts.VeryLong);
            return await _httpClient.GetStringAsync("/ipc/certs/root.pem", cts.Token);
        }, ct);
    }

    public async Task<byte[]> GetRootCertDerAsync(CancellationToken ct = default)
    {
        return await RetryAsync(async () =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(IpcTimeouts.VeryLong);
            return await _httpClient.GetByteArrayAsync("/ipc/certs/root.der", cts.Token);
        }, ct);
    }

    public async Task<byte[]> GetRootCertPfxAsync(CancellationToken ct = default)
    {
        return await RetryAsync(async () =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(IpcTimeouts.VeryLong);
            return await _httpClient.GetByteArrayAsync("/ipc/certs/root.pfx", cts.Token);
        }, ct);
    }

    public async Task<IReadOnlyList<SessionLogEntry>> DrainSessionLogAsync(CancellationToken ct = default)
    {
        return await RetryAsync(async () =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(IpcTimeouts.Medium);
            var response = await _httpClient.PostAsync("/ipc/session-log/drain", null, cts.Token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<IReadOnlyList<SessionLogEntry>>(_jsonOptions, cts.Token)
                ?? Array.Empty<SessionLogEntry>();
        }, ct);
    }

    public async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(IpcTimeouts.Small);
            var status = await GetStatusAsync(cts.Token);
            return status.IsListening;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Health check failed");
            return false;
        }
    }

    private async Task<T> RetryAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        var delay = BaseDelay;
        var attempt = 0;

        while (true)
        {
            try
            {
                return await action();
            }
            catch (HttpRequestException ex) when (attempt < MaxRetries && IsTransient(ex))
            {
                attempt++;
                _logger.LogDebug(ex, "Transient error (attempt {Attempt}/{MaxRetries}), retrying in {Delay}ms",
                    attempt, MaxRetries, delay.TotalMilliseconds);

                await Task.Delay(delay, ct);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, MaxDelay.TotalMilliseconds));
            }
            catch (TaskCanceledException ex) when (attempt < MaxRetries)
            {
                attempt++;
                _logger.LogDebug(ex, "Timeout (attempt {Attempt}/{MaxRetries}), retrying in {Delay}ms",
                    attempt, MaxRetries, delay.TotalMilliseconds);

                await Task.Delay(delay, ct);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, MaxDelay.TotalMilliseconds));
            }
        }
    }

    private static bool IsTransient(HttpRequestException ex)
    {
        if (ex.StatusCode == null) return true;
        return (int)ex.StatusCode >= 500 || (int)ex.StatusCode == 408 || (int)ex.StatusCode == 429;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _httpClient.Dispose();
        _disposed = true;
    }
}
