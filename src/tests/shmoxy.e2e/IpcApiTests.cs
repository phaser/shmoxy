using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using shmoxy.models.configuration;
using shmoxy.server;
using shmoxy.server.hooks;
using shmoxy.ipc;
using Xunit;

namespace shmoxy.e2e;

[Trait("Category", "Integration")]
public class IpcApiTests : IAsyncLifetime
{
    private ProxyServer? _server;
    private InspectionHook? _inspectionHook;
    private CancellationTokenSource? _cts;
    private IWebHost? _ipcHost;
    private string _socketPath = null!;
    private HttpClient? _ipcClient;

    public async Task InitializeAsync()
    {
        _socketPath = Path.Combine(Path.GetTempPath(), $"shmoxy-test-{Guid.NewGuid():N}.sock");
        
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
        
        _ipcHost = ShmoxyHost.CreateIpcHost(stateService, config, _socketPath);

        await _ipcHost.StartAsync(_cts.Token);
        
        Console.WriteLine($"IPC API started on {_socketPath}");
        
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (context, token) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                var endPoint = new UnixDomainSocketEndPoint(_socketPath);
                await socket.ConnectAsync(endPoint, token);
                return new NetworkStream(socket, ownsSocket: true);
            }
        };
        
        _ipcClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
    }

    public async Task DisposeAsync()
    {
        _ipcClient?.Dispose();
        
        if (_ipcHost != null)
        {
            await _ipcHost.StopAsync(TimeSpan.Zero);
            _ipcHost.Dispose();
        }
        
        if (_cts != null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
        }
        
        _server?.Dispose();
        
        if (!string.IsNullOrEmpty(_socketPath) && File.Exists(_socketPath))
        {
            File.Delete(_socketPath);
        }
    }

    [Fact]
    public async Task Status_Endpoint_ReturnsProxyHealth()
    {
        var response = await _ipcClient!.GetAsync("/ipc/status");
        
        Assert.Equal(200, (int)response.StatusCode);
        
        var json = await response.Content.ReadAsStringAsync();
        var status = JsonSerializer.Deserialize<JsonElement>(json);
        
        Assert.True(status.GetProperty("isListening").GetBoolean());
        Assert.Equal(_server!.ListeningPort, status.GetProperty("port").GetInt32());
        Assert.True(status.GetProperty("uptime").GetString()?.Contains(":") ?? false);
        Assert.Equal(0, status.GetProperty("activeConnections").GetInt32());
    }

    [Fact]
    public async Task Config_Endpoint_ReturnsCurrentConfig()
    {
        var response = await _ipcClient!.GetAsync("/ipc/config");
        
        Assert.Equal(200, (int)response.StatusCode);
        
        var json = await response.Content.ReadAsStringAsync();
        var config = JsonSerializer.Deserialize<JsonElement>(json);
        
        Assert.Equal(0, config.GetProperty("port").GetInt32());
        Assert.Equal(0, config.GetProperty("logLevel").GetInt32());
    }

    [Fact]
    public async Task Config_Endpoint_UpdateLogLevel()
    {
        var newConfig = new { port = _server!.ListeningPort, certPath = (string?)null, keyPath = (string?)null, logLevel = 1 };
        var content = new StringContent(JsonSerializer.Serialize(newConfig), System.Text.Encoding.UTF8, "application/json");
        
        var response = await _ipcClient!.PutAsync("/ipc/config", content);
        
        Assert.Equal(200, (int)response.StatusCode);
        
        var json = await response.Content.ReadAsStringAsync();
        var updated = JsonSerializer.Deserialize<JsonElement>(json);
        
        Assert.Equal(1, updated.GetProperty("logLevel").GetInt32());
    }

    [Fact]
    public async Task Hooks_Endpoint_ListsInspectionHook()
    {
        var response = await _ipcClient!.GetAsync("/ipc/hooks");
        
        Assert.Equal(200, (int)response.StatusCode);
        
        var json = await response.Content.ReadAsStringAsync();
        var hooks = JsonSerializer.Deserialize<JsonElement>(json);
        
        Assert.True(hooks.GetArrayLength() > 0);
        
        var inspectionHook = hooks.EnumerateArray().First(h => h.GetProperty("id").GetString() == "inspection");
        Assert.Equal("Request/Response Inspection", inspectionHook.GetProperty("name").GetString());
        Assert.Equal("builtin", inspectionHook.GetProperty("type").GetString());
        Assert.False(inspectionHook.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task Inspection_Enable_Disable_TogglesHook()
    {
        var enableResponse = await _ipcClient!.PostAsync("/ipc/inspect/enable", null);
        Assert.Equal(200, (int)enableResponse.StatusCode);
        
        var hooksResponse = await _ipcClient.GetAsync("/ipc/hooks");
        var hooksJson = await hooksResponse.Content.ReadAsStringAsync();
        var hooks = JsonSerializer.Deserialize<JsonElement>(hooksJson);
        var inspectionHook = hooks.EnumerateArray().First(h => h.GetProperty("id").GetString() == "inspection");
        Assert.True(inspectionHook.GetProperty("enabled").GetBoolean());
        
        var disableResponse = await _ipcClient.PostAsync("/ipc/inspect/disable", null);
        Assert.Equal(200, (int)disableResponse.StatusCode);
        
        hooksResponse = await _ipcClient.GetAsync("/ipc/hooks");
        hooksJson = await hooksResponse.Content.ReadAsStringAsync();
        hooks = JsonSerializer.Deserialize<JsonElement>(hooksJson);
        inspectionHook = hooks.EnumerateArray().First(h => h.GetProperty("id").GetString() == "inspection");
        Assert.False(inspectionHook.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task Shutdown_Endpoint_ReturnsSuccess()
    {
        var response = await _ipcClient!.PostAsync("/ipc/shutdown", null);
        
        Assert.Equal(200, (int)response.StatusCode);
        
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(json);
        
        Assert.True(result.GetProperty("success").GetBoolean());
        Assert.Contains("Shutdown initiated", result.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Hook_EnableDisable_Endpoints_Work()
    {
        var enableResponse = await _ipcClient!.PostAsync("/ipc/hooks/inspection/enable", null);
        Assert.Equal(200, (int)enableResponse.StatusCode);
        
        var enableJson = await enableResponse.Content.ReadAsStringAsync();
        var enableResult = JsonSerializer.Deserialize<JsonElement>(enableJson);
        Assert.True(enableResult.GetProperty("success").GetBoolean());
        
        var disableResponse = await _ipcClient.PostAsync("/ipc/hooks/inspection/disable", null);
        Assert.Equal(200, (int)disableResponse.StatusCode);
        
        var disableJson = await disableResponse.Content.ReadAsStringAsync();
        var disableResult = JsonSerializer.Deserialize<JsonElement>(disableJson);
        Assert.True(disableResult.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Hook_EnableDisable_UnknownHook_ReturnsFailure()
    {
        var enableResponse = await _ipcClient!.PostAsync("/ipc/hooks/unknown/enable", null);
        Assert.Equal(200, (int)enableResponse.StatusCode);
        
        var enableJson = await enableResponse.Content.ReadAsStringAsync();
        var enableResult = JsonSerializer.Deserialize<JsonElement>(enableJson);
        Assert.False(enableResult.GetProperty("success").GetBoolean());
        Assert.Contains("Unknown hook", enableResult.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Certs_RootPem_Endpoint_ReturnsCertificate()
    {
        var response = await _ipcClient!.GetAsync("/ipc/certs/root.pem");
        
        Assert.Equal(200, (int)response.StatusCode);
        Assert.Contains("pem", response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant());
        
        var pem = await response.Content.ReadAsStringAsync();
        Assert.Contains("-----BEGIN CERTIFICATE-----", pem);
        Assert.Contains("-----END CERTIFICATE-----", pem);
    }

    [Fact]
    public async Task Certs_RootDer_Endpoint_ReturnsCertificate()
    {
        var response = await _ipcClient!.GetAsync("/ipc/certs/root.der");
        
        Assert.Equal(200, (int)response.StatusCode);
        Assert.Contains("x-x509", response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant());
        
        var der = await response.Content.ReadAsByteArrayAsync();
        Assert.True(der.Length > 0);
    }
}
