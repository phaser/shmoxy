using System.Net.Http.Json;

namespace shmoxy.frontend.services;

public class ApiClient(HttpClient httpClient)
{
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public async Task<FrontendProxyConfig> GetProxyConfigAsync()
    {
        var response = await _httpClient.GetAsync("/api/proxy-config");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return FrontendProxyConfig.Default;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<FrontendProxyConfig>() ?? FrontendProxyConfig.Default;
    }

    public async Task SaveProxyConfigAsync(FrontendProxyConfig config)
    {
        var response = await _httpClient.PutAsJsonAsync("/api/proxy-config", config);
        response.EnsureSuccessStatusCode();
    }

    public async Task<FrontendProxyStatus> GetProxyStatusAsync()
    {
        var response = await _httpClient.GetAsync("/api/proxies");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return FrontendProxyStatus.Stopped;

        response.EnsureSuccessStatusCode();
        var proxies = await response.Content.ReadFromJsonAsync<List<ProxyInfo>>();
        return proxies?.FirstOrDefault() is ProxyInfo pi
            ? new FrontendProxyStatus(IsRunning: true, Address: $"{pi.Host}:{pi.Port}")
            : FrontendProxyStatus.Stopped;
    }

    public async Task<List<InspectionRequestInfo>> GetRequestHistoryAsync()
    {
        var response = await _httpClient.GetAsync("/api/inspection");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return [];

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<InspectionRequestInfo>>() ?? [];
    }
}
