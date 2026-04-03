using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using shmoxy.server;
using shmoxy.server.hooks;
using shmoxy.ipc;
using shmoxy.shared.ipc;
using Xunit;

namespace shmoxy.e2e;

[Trait("Category", "Integration")]
public class IpcApiTests : IAsyncLifetime
{
    private ProxyServer? _server;
    private InspectionHook? _inspectionHook;
    private BreakpointHook? _breakpointHook;
    private CancellationTokenSource? _cts;
    private IHost? _ipcHost;
    private string _socketPath = null!;
    private HttpClient? _ipcClient;

    public async Task InitializeAsync()
    {
        _socketPath = Path.Combine(Path.GetTempPath(), $"shmoxy-test-{Guid.NewGuid():N}.sock");

        var config = new ProxyConfig { Port = 0, LogLevel = ProxyConfig.LogLevelEnum.Debug };
        _inspectionHook = new InspectionHook();
        _breakpointHook = new BreakpointHook();
        var hookChain = new InterceptHookChain().Add(_inspectionHook).Add(_breakpointHook);

        _server = new ProxyServer(config, hookChain);
        _cts = new CancellationTokenSource();

        _ = _server.StartAsync(_cts.Token);

        for (var i = 0; i < 20 && !_server.IsListening; i++)
            await Task.Delay(50);

        if (!_server.IsListening)
            throw new InvalidOperationException("Proxy server failed to start");

        Console.WriteLine($"Proxy started on port {_server.ListeningPort}");

        var stateService = new ProxyStateService(_server, _inspectionHook, breakpointHook: _breakpointHook);

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
            await _ipcHost.StopAsync(_cts!.Token);
            _ipcHost.Dispose();
        }

        if (_cts != null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
        }

        if (_server != null)
            await _server.DisposeAsync();

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
    public async Task Config_Endpoint_UpdateAllFields()
    {
        var newConfig = new
        {
            port = 9999,
            logLevel = 2, // Warn
            passthroughHosts = new[] { "example.com", "*.test.com" }
        };
        var content = new StringContent(JsonSerializer.Serialize(newConfig), System.Text.Encoding.UTF8, "application/json");

        var response = await _ipcClient!.PutAsync("/ipc/config", content);

        Assert.Equal(200, (int)response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        var updated = JsonSerializer.Deserialize<JsonElement>(json);

        Assert.Equal(9999, updated.GetProperty("port").GetInt32());
        Assert.Equal(2, updated.GetProperty("logLevel").GetInt32());

        var hosts = updated.GetProperty("passthroughHosts").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("example.com", hosts);
        Assert.Contains("*.test.com", hosts);
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
        Assert.True(inspectionHook.GetProperty("enabled").GetBoolean());
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

    // --- Breakpoint IPC endpoint tests ---

    [Fact]
    public async Task Breakpoints_Enable_Disable_TogglesState()
    {
        var enableResponse = await _ipcClient!.PostAsync("/ipc/breakpoints/enable", null);
        Assert.Equal(200, (int)enableResponse.StatusCode);

        var enableJson = await enableResponse.Content.ReadAsStringAsync();
        var enableResult = JsonSerializer.Deserialize<JsonElement>(enableJson);
        Assert.True(enableResult.GetProperty("success").GetBoolean());
        Assert.True(_breakpointHook!.Enabled);

        var disableResponse = await _ipcClient.PostAsync("/ipc/breakpoints/disable", null);
        Assert.Equal(200, (int)disableResponse.StatusCode);

        var disableJson = await disableResponse.Content.ReadAsStringAsync();
        var disableResult = JsonSerializer.Deserialize<JsonElement>(disableJson);
        Assert.True(disableResult.GetProperty("success").GetBoolean());
        Assert.False(_breakpointHook.Enabled);
    }

    [Fact]
    public async Task Breakpoints_Paused_ReturnsEmptyWhenNoPausedRequests()
    {
        var response = await _ipcClient!.GetAsync("/ipc/breakpoints/paused");

        Assert.Equal(200, (int)response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        var paused = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal(0, paused.GetArrayLength());
    }

    [Fact]
    public async Task Breakpoints_Release_ReturnsNotFoundForUnknownId()
    {
        var response = await _ipcClient!.PostAsync("/ipc/breakpoints/unknown-id/release", null);

        Assert.Equal(200, (int)response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Contains("not found", result.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Breakpoints_Drop_ReturnsNotFoundForUnknownId()
    {
        var response = await _ipcClient!.PostAsync("/ipc/breakpoints/unknown-id/drop", null);

        Assert.Equal(200, (int)response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Contains("not found", result.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Breakpoints_Rules_ReturnsEmptyByDefault()
    {
        var response = await _ipcClient!.GetAsync("/ipc/breakpoints/rules");

        Assert.Equal(200, (int)response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        var rules = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal(0, rules.GetArrayLength());
    }

    [Fact]
    public async Task Breakpoints_AddRule_CreatesAndReturnsRule()
    {
        var rulePayload = new { method = "GET", urlPattern = "example.com" };
        var content = new StringContent(JsonSerializer.Serialize(rulePayload), System.Text.Encoding.UTF8, "application/json");

        var response = await _ipcClient!.PostAsync("/ipc/breakpoints/rules", content);

        Assert.Equal(200, (int)response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        var rule = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.False(string.IsNullOrEmpty(rule.GetProperty("id").GetString()));
        Assert.Equal("GET", rule.GetProperty("method").GetString());
        Assert.Equal("example.com", rule.GetProperty("urlPattern").GetString());

        // Verify rule appears in list
        var listResponse = await _ipcClient.GetAsync("/ipc/breakpoints/rules");
        var listJson = await listResponse.Content.ReadAsStringAsync();
        var rules = JsonSerializer.Deserialize<JsonElement>(listJson);
        Assert.True(rules.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Breakpoints_RemoveRule_DeletesExistingRule()
    {
        // Add a rule first
        var rulePayload = new { method = "POST", urlPattern = "test.com" };
        var content = new StringContent(JsonSerializer.Serialize(rulePayload), System.Text.Encoding.UTF8, "application/json");
        var addResponse = await _ipcClient!.PostAsync("/ipc/breakpoints/rules", content);
        var addJson = await addResponse.Content.ReadAsStringAsync();
        var addedRule = JsonSerializer.Deserialize<JsonElement>(addJson);
        var ruleId = addedRule.GetProperty("id").GetString()!;

        // Delete the rule
        var deleteResponse = await _ipcClient.DeleteAsync($"/ipc/breakpoints/rules/{ruleId}");

        Assert.Equal(200, (int)deleteResponse.StatusCode);

        var deleteJson = await deleteResponse.Content.ReadAsStringAsync();
        var deleteResult = JsonSerializer.Deserialize<JsonElement>(deleteJson);
        Assert.True(deleteResult.GetProperty("success").GetBoolean());

        // Verify rule no longer in list
        var listResponse = await _ipcClient.GetAsync("/ipc/breakpoints/rules");
        var listJson = await listResponse.Content.ReadAsStringAsync();
        var rules = JsonSerializer.Deserialize<JsonElement>(listJson);
        var ruleIds = rules.EnumerateArray().Select(r => r.GetProperty("id").GetString()).ToList();
        Assert.DoesNotContain(ruleId, ruleIds);
    }

    [Fact]
    public async Task Breakpoints_RemoveRule_ReturnsFalseForUnknownId()
    {
        var response = await _ipcClient!.DeleteAsync("/ipc/breakpoints/rules/nonexistent-id");

        Assert.Equal(200, (int)response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.False(result.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Breakpoints_FullLifecycle_AddRule_Enable_PauseRequest_Release()
    {
        // 1. Add a breakpoint rule matching all requests
        var rulePayload = new { urlPattern = "" };
        var ruleContent = new StringContent(JsonSerializer.Serialize(rulePayload), System.Text.Encoding.UTF8, "application/json");
        var addRuleResponse = await _ipcClient!.PostAsync("/ipc/breakpoints/rules", ruleContent);
        Assert.Equal(200, (int)addRuleResponse.StatusCode);

        // Adding a rule auto-enables breakpoints
        Assert.True(_breakpointHook!.Enabled);

        // 2. Send a request through the breakpoint hook directly to simulate a paused request
        var testRequest = new shmoxy.models.dto.InterceptedRequest
        {
            Method = "GET",
            Url = new Uri("http://example.com/test"),
            Host = "example.com",
            Path = "/test",
            CorrelationId = Guid.NewGuid().ToString()
        };

        // Start the request in background — it will block until released
        var pausedTask = _breakpointHook.OnRequestAsync(testRequest);

        // Wait briefly for the request to be paused
        await Task.Delay(100);

        // 3. Verify the request appears in paused list
        var pausedResponse = await _ipcClient.GetAsync("/ipc/breakpoints/paused");
        var pausedJson = await pausedResponse.Content.ReadAsStringAsync();
        var pausedList = JsonSerializer.Deserialize<JsonElement>(pausedJson);
        Assert.True(pausedList.GetArrayLength() >= 1);

        var pausedReq = pausedList.EnumerateArray().First(
            p => p.GetProperty("correlationId").GetString() == testRequest.CorrelationId);
        Assert.Equal("GET", pausedReq.GetProperty("method").GetString());
        Assert.Contains("example.com/test", pausedReq.GetProperty("url").GetString());

        // 4. Release the request
        var releaseResponse = await _ipcClient.PostAsync(
            $"/ipc/breakpoints/{testRequest.CorrelationId}/release", null);
        var releaseJson = await releaseResponse.Content.ReadAsStringAsync();
        var releaseResult = JsonSerializer.Deserialize<JsonElement>(releaseJson);
        Assert.True(releaseResult.GetProperty("success").GetBoolean());

        // 5. Verify the hook unblocks and returns the request
        var result = await pausedTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(result);
        Assert.Equal(testRequest.CorrelationId, result!.CorrelationId);
    }

    [Fact]
    public async Task Breakpoints_FullLifecycle_PauseRequest_Drop()
    {
        // Enable breakpoints with a catch-all rule
        var rulePayload = new { urlPattern = "" };
        var ruleContent = new StringContent(JsonSerializer.Serialize(rulePayload), System.Text.Encoding.UTF8, "application/json");
        await _ipcClient!.PostAsync("/ipc/breakpoints/rules", ruleContent);

        // Send a request that will be paused
        var testRequest = new shmoxy.models.dto.InterceptedRequest
        {
            Method = "POST",
            Url = new Uri("http://example.com/drop-test"),
            Host = "example.com",
            Path = "/drop-test",
            CorrelationId = Guid.NewGuid().ToString()
        };

        var pausedTask = _breakpointHook!.OnRequestAsync(testRequest);
        await Task.Delay(100);

        // Drop the request
        var dropResponse = await _ipcClient.PostAsync(
            $"/ipc/breakpoints/{testRequest.CorrelationId}/drop", null);
        var dropJson = await dropResponse.Content.ReadAsStringAsync();
        var dropResult = JsonSerializer.Deserialize<JsonElement>(dropJson);
        Assert.True(dropResult.GetProperty("success").GetBoolean());

        // Verify the hook unblocks and returns null (dropped)
        var result = await pausedTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(result);
    }
}
