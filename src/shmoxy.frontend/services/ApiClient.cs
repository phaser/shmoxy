using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using shmoxy.frontend.models;

namespace shmoxy.frontend.services;

public class ApiClient(HttpClient httpClient)
{
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public async Task<FrontendProxyConfig> GetProxyConfigAsync()
    {
        var response = await _httpClient.GetAsync("/api/proxies/local/config");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound
            || response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            return FrontendProxyConfig.Default;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<FrontendProxyConfig>() ?? FrontendProxyConfig.Default;
    }

    public async Task SaveProxyConfigAsync(FrontendProxyConfig config)
    {
        var response = await _httpClient.PutAsJsonAsync("/api/proxies/local/config", config);
        await EnsureSuccessOrThrowWithBody(response);
    }

    public async Task<FrontendProxyStatus> GetProxyStatusAsync()
    {
        var response = await _httpClient.GetAsync("/api/proxies/local");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return FrontendProxyStatus.Stopped;

        response.EnsureSuccessStatusCode();
        var state = await response.Content.ReadFromJsonAsync<ProxyInstanceStateDto>();
        if (state is null)
            return FrontendProxyStatus.Stopped;

        var isRunning = string.Equals(state.State, "Running", StringComparison.OrdinalIgnoreCase);
        var address = state.Port.HasValue ? $"localhost:{state.Port}" : null;
        return new FrontendProxyStatus(IsRunning: isRunning, Address: address, ProxyVersion: state.ProxyVersion);
    }

    public async Task<FrontendProxyStatus> StartProxyAsync()
    {
        var response = await _httpClient.PostAsync("/api/proxies/local/start", null);
        await EnsureSuccessOrThrowWithBody(response);
        var state = await response.Content.ReadFromJsonAsync<ProxyInstanceStateDto>();
        if (state is null)
            return FrontendProxyStatus.Stopped;

        var isRunning = string.Equals(state.State, "Running", StringComparison.OrdinalIgnoreCase);
        var address = state.Port.HasValue ? $"localhost:{state.Port}" : null;
        return new FrontendProxyStatus(IsRunning: isRunning, Address: address, ProxyVersion: state.ProxyVersion);
    }

    public async Task StopProxyAsync()
    {
        var response = await _httpClient.PostAsync("/api/proxies/local/stop", null);
        await EnsureSuccessOrThrowWithBody(response);
    }

    private static async Task EnsureSuccessOrThrowWithBody(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync();

        // Try to extract error details from JSON error responses
        try
        {
            using var doc = JsonDocument.Parse(body);
            string? message = null;
            string? error = null;

            if (doc.RootElement.TryGetProperty("message", out var msgProp)
                || doc.RootElement.TryGetProperty("Message", out msgProp))
            {
                message = msgProp.GetString();
            }

            if (doc.RootElement.TryGetProperty("error", out var errProp)
                || doc.RootElement.TryGetProperty("Error", out errProp))
            {
                error = errProp.GetString();
            }

            if (message is not null)
            {
                var detail = error is not null ? $"{message}: {error}" : message;
                throw new HttpRequestException(detail);
            }
        }
        catch (JsonException)
        {
            // Not JSON — fall through
        }

        throw new HttpRequestException(string.IsNullOrWhiteSpace(body)
            ? $"Request failed with status {(int)response.StatusCode}"
            : body);
    }

    public async Task<List<DetectorInfo>> GetDetectorsAsync()
    {
        var response = await _httpClient.GetAsync("/api/proxies/local/detectors");
        if (!response.IsSuccessStatusCode)
            return [];

        return await response.Content.ReadFromJsonAsync<List<DetectorInfo>>() ?? [];
    }

    public async Task EnableDetectorAsync(string detectorId)
    {
        var response = await _httpClient.PostAsync($"/api/proxies/local/detectors/{detectorId}/enable", null);
        await EnsureSuccessOrThrowWithBody(response);
    }

    public async Task DisableDetectorAsync(string detectorId)
    {
        var response = await _httpClient.PostAsync($"/api/proxies/local/detectors/{detectorId}/disable", null);
        await EnsureSuccessOrThrowWithBody(response);
    }

    public async Task<List<PassthroughSuggestionDto>> GetSuggestionsAsync()
    {
        var response = await _httpClient.GetAsync("/api/proxies/local/detectors/suggestions");
        if (!response.IsSuccessStatusCode)
            return [];

        return await response.Content.ReadFromJsonAsync<List<PassthroughSuggestionDto>>() ?? [];
    }

    public async Task AcceptSuggestionAsync(string host)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/proxies/local/detectors/suggestions/accept", new { Host = host });
        await EnsureSuccessOrThrowWithBody(response);
    }

    public async Task DismissSuggestionAsync(string host)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/proxies/local/detectors/suggestions/dismiss", new { Host = host });
        await EnsureSuccessOrThrowWithBody(response);
    }

    public async Task<List<TemporaryPassthroughEntryDto>> GetTempPassthroughAsync()
    {
        var response = await _httpClient.GetAsync("/api/proxies/local/detectors/temp-passthrough");
        if (!response.IsSuccessStatusCode)
            return [];

        return await response.Content.ReadFromJsonAsync<List<TemporaryPassthroughEntryDto>>() ?? [];
    }

    public async Task<List<InspectionRequestInfo>> GetRequestHistoryAsync()
    {
        var response = await _httpClient.GetAsync("/api/inspection");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return [];

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<InspectionRequestInfo>>() ?? [];
    }

    public async Task<List<SessionSummary>> ListSessionsAsync()
    {
        var response = await _httpClient.GetAsync("/api/sessions");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<SessionSummary>>() ?? [];
    }

    public async Task<SessionSummary> CreateSessionAsync(string name, List<SessionRowData> rows)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/sessions", new { Name = name, Rows = rows });
        await EnsureSuccessOrThrowWithBody(response);
        return await response.Content.ReadFromJsonAsync<SessionSummary>()
            ?? throw new HttpRequestException("Failed to create session");
    }

    public async Task<List<SessionRowData>> LoadSessionRowsAsync(string sessionId)
    {
        var response = await _httpClient.GetAsync($"/api/sessions/{sessionId}");
        await EnsureSuccessOrThrowWithBody(response);
        return await response.Content.ReadFromJsonAsync<List<SessionRowData>>() ?? [];
    }

    public async Task<SessionSummary> UpdateSessionAsync(string sessionId, List<SessionRowData> rows)
    {
        var response = await _httpClient.PutAsJsonAsync($"/api/sessions/{sessionId}", new { Rows = rows });
        await EnsureSuccessOrThrowWithBody(response);
        return await response.Content.ReadFromJsonAsync<SessionSummary>()
            ?? throw new HttpRequestException("Failed to update session");
    }

    public async Task DeleteSessionAsync(string sessionId)
    {
        var response = await _httpClient.DeleteAsync($"/api/sessions/{sessionId}");
        await EnsureSuccessOrThrowWithBody(response);
    }

    public async IAsyncEnumerable<InspectionEventDto> StreamInspectionEventsAsync(
        string proxyId = "local",
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/proxies/{proxyId}/inspect/stream");
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
                break;

            if (!line.StartsWith("data: ", StringComparison.Ordinal))
                continue;

            var json = line["data: ".Length..];
            var evt = JsonSerializer.Deserialize<InspectionEventDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (evt is not null)
                yield return evt;
        }
    }
}
