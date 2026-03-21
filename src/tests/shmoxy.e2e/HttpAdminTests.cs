using System.Net.Http;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using shmoxy.models.configuration;
using shmoxy.server;
using shmoxy.server.hooks;
using shmoxy.ipc;
using Xunit;

namespace shmoxy.e2e;

[Trait("Category", "Integration")]
public class HttpAdminTests : IAsyncLifetime
{
    private ProxyServer? _server;
    private InspectionHook? _inspectionHook;
    private CancellationTokenSource? _cts;
    private IWebHost? _adminHost;
    private HttpClient? _adminClient;
    private string? _apiKey;

    public async Task InitializeAsync()
    {
        var config = new ProxyConfig { Port = 0, LogLevel = ProxyConfig.LogLevelEnum.Debug };
        _inspectionHook = new InspectionHook();
        var hookChain = new InterceptHookChain().Add(_inspectionHook);
        
        _server = new ProxyServer(config, hookChain);
        _cts = new CancellationTokenSource();
        
        _ = _server.StartAsync(_cts.Token);
        
        for (var i = 0; i < 20 && !_server.IsListening; i++)
            await Task.Delay(50);

        if (!_server.IsListening)
            throw new InvalidOperationException("Proxy server failed to start");

        Console.WriteLine($"Proxy started on port {_server.ListeningPort}");
        
        var stateService = new ProxyStateService(_server, _inspectionHook);
        
        var apiKeyService = new ApiKeyService { ApiKey = GenerateApiKey() };
        _apiKey = apiKeyService.ApiKey;
        
        var testPort = 9091;
        
        _adminHost = new WebHostBuilder()
            .UseKestrel(kestrelOptions =>
            {
                kestrelOptions.ListenAnyIP(testPort);
            })
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton(stateService);
                services.AddSingleton(apiKeyService);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseMiddleware<ApiKeyAuthenticationMiddleware>();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapProxyControlApi(stateService, config);
                });
            })
            .Build();

        await _adminHost.StartAsync(_cts.Token);
        
        Console.WriteLine($"Admin API started on port {testPort}, API key: {_apiKey}");
        
        _adminClient = new HttpClient { BaseAddress = new System.Uri($"http://localhost:{testPort}") };
    }

    public async Task DisposeAsync()
    {
        _adminClient?.Dispose();
        
        if (_adminHost != null)
        {
            await _adminHost.StopAsync(TimeSpan.Zero);
            _adminHost.Dispose();
        }
        
        if (_cts != null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
        }
        
        _server?.Dispose();
    }

    [Fact]
    public async Task AdminApi_WithoutApiKey_Returns401()
    {
        var response = await _adminClient!.GetAsync("/ipc/status");
        
        Assert.Equal(401, (int)response.StatusCode);
        
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Unauthorized", body);
    }

    [Fact]
    public async Task AdminApi_WithValidApiKey_Returns200()
    {
        _adminClient!.DefaultRequestHeaders.Add("X-API-Key", _apiKey);
        
        var response = await _adminClient.GetAsync("/ipc/status");
        
        Assert.Equal(200, (int)response.StatusCode);
        
        var json = await response.Content.ReadAsStringAsync();
        var status = JsonSerializer.Deserialize<JsonElement>(json);
        
        Assert.True(status.GetProperty("isListening").GetBoolean());
    }

    [Fact]
    public async Task AdminApi_WithInvalidApiKey_Returns401()
    {
        _adminClient!.DefaultRequestHeaders.Add("X-API-Key", "invalid-key");
        
        var response = await _adminClient.GetAsync("/ipc/status");
        
        Assert.Equal(401, (int)response.StatusCode);
    }

    [Fact]
    public async Task AdminApi_CertsEndpoint_RequiresAuth()
    {
        var response = await _adminClient!.GetAsync("/ipc/certs/root.pem");
        
        Assert.Equal(401, (int)response.StatusCode);
    }

    [Fact]
    public async Task AdminApi_CertsEndpoint_WithAuth_ReturnsCertificate()
    {
        _adminClient!.DefaultRequestHeaders.Add("X-API-Key", _apiKey);
        
        var response = await _adminClient.GetAsync("/ipc/certs/root.pem");
        
        Assert.Equal(200, (int)response.StatusCode);
        
        var pem = await response.Content.ReadAsStringAsync();
        Assert.Contains("-----BEGIN CERTIFICATE-----", pem);
        Assert.Contains("-----END CERTIFICATE-----", pem);
    }

    [Fact]
    public async Task AdminApi_HooksEndpoint_RequiresAuth()
    {
        var response = await _adminClient!.GetAsync("/ipc/hooks");
        
        Assert.Equal(401, (int)response.StatusCode);
    }

    [Fact]
    public async Task AdminApi_HooksEndpoint_WithAuth_ReturnsHooks()
    {
        _adminClient!.DefaultRequestHeaders.Add("X-API-Key", _apiKey);
        
        var response = await _adminClient.GetAsync("/ipc/hooks");
        
        Assert.Equal(200, (int)response.StatusCode);
        
        var json = await response.Content.ReadAsStringAsync();
        var hooks = JsonSerializer.Deserialize<JsonElement>(json);
        
        Assert.True(hooks.GetArrayLength() > 0);
    }

    private static string GenerateApiKey()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
