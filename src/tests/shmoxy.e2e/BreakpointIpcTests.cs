using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using shmoxy.server;
using shmoxy.server.hooks;
using shmoxy.ipc;
using shmoxy.shared.ipc;
using Xunit;

namespace shmoxy.e2e;

[Trait("Category", "Integration")]
public class BreakpointIpcTests : IAsyncLifetime
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
        _socketPath = Path.Combine(Path.GetTempPath(), $"shmoxy-bp-test-{Guid.NewGuid():N}.sock");

        var config = new ProxyConfig { Port = 0, LogLevel = ProxyConfig.LogLevelEnum.Debug };
        _inspectionHook = new InspectionHook();
        _breakpointHook = new BreakpointHook();
        var hookChain = new InterceptHookChain()
            .Add(_inspectionHook)
            .Add(_breakpointHook);

        _server = new ProxyServer(config, hookChain);
        _cts = new CancellationTokenSource();

        _ = _server.StartAsync(_cts.Token);

        for (var i = 0; i < 20 && !_server.IsListening; i++)
            await Task.Delay(50);

        if (!_server.IsListening)
            throw new InvalidOperationException("Proxy server failed to start");

        var stateService = new ProxyStateService(_server, _inspectionHook, breakpointHook: _breakpointHook);

        _ipcHost = ShmoxyHost.CreateIpcHost(stateService, config, _socketPath);
        await _ipcHost.StartAsync(_cts.Token);

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

        _server?.Dispose();

        if (!string.IsNullOrEmpty(_socketPath) && File.Exists(_socketPath))
        {
            File.Delete(_socketPath);
        }
    }

    [Fact]
    public async Task Breakpoints_Enable_ReturnsSuccess()
    {
        var response = await _ipcClient!.PostAsync("/ipc/breakpoints/enable", null);

        Assert.Equal(200, (int)response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(json);

        Assert.True(result.GetProperty("success").GetBoolean());
        Assert.True(_breakpointHook!.Enabled);
    }

    [Fact]
    public async Task Breakpoints_Disable_ReturnsSuccess()
    {
        _breakpointHook!.Enabled = true;

        var response = await _ipcClient!.PostAsync("/ipc/breakpoints/disable", null);

        Assert.Equal(200, (int)response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(json);

        Assert.True(result.GetProperty("success").GetBoolean());
        Assert.False(_breakpointHook.Enabled);
    }

    [Fact]
    public async Task Breakpoints_EnableDisable_TogglesState()
    {
        Assert.False(_breakpointHook!.Enabled);

        await _ipcClient!.PostAsync("/ipc/breakpoints/enable", null);
        Assert.True(_breakpointHook.Enabled);

        await _ipcClient.PostAsync("/ipc/breakpoints/disable", null);
        Assert.False(_breakpointHook.Enabled);
    }

    [Fact]
    public async Task Breakpoints_Paused_ReturnsEmptyInitially()
    {
        var response = await _ipcClient!.GetAsync("/ipc/breakpoints/paused");

        Assert.Equal(200, (int)response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        var paused = JsonSerializer.Deserialize<JsonElement>(json);

        Assert.Equal(0, paused.GetArrayLength());
    }

    [Fact]
    public async Task Breakpoints_Rules_ReturnsEmptyInitially()
    {
        var response = await _ipcClient!.GetAsync("/ipc/breakpoints/rules");

        Assert.Equal(200, (int)response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        var rules = JsonSerializer.Deserialize<JsonElement>(json);

        Assert.Equal(0, rules.GetArrayLength());
    }

    [Fact]
    public async Task Breakpoints_AddRule_ReturnsRuleWithId()
    {
        var ruleBody = new { method = "GET", urlPattern = "example.com" };
        var content = new StringContent(JsonSerializer.Serialize(ruleBody), Encoding.UTF8, "application/json");

        var response = await _ipcClient!.PostAsync("/ipc/breakpoints/rules", content);

        Assert.Equal(200, (int)response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        var rule = JsonSerializer.Deserialize<JsonElement>(json);

        Assert.False(string.IsNullOrEmpty(rule.GetProperty("id").GetString()));
        Assert.Equal("GET", rule.GetProperty("method").GetString());
        Assert.Equal("example.com", rule.GetProperty("urlPattern").GetString());
    }

    [Fact]
    public async Task Breakpoints_AddRule_AutoEnablesBreakpoints()
    {
        Assert.False(_breakpointHook!.Enabled);

        var ruleBody = new { method = "POST", urlPattern = "/api" };
        var content = new StringContent(JsonSerializer.Serialize(ruleBody), Encoding.UTF8, "application/json");

        await _ipcClient!.PostAsync("/ipc/breakpoints/rules", content);

        Assert.True(_breakpointHook.Enabled);
    }

    [Fact]
    public async Task Breakpoints_AddAndListRules()
    {
        var rule1 = new { method = "GET", urlPattern = "example.com" };
        var rule2 = new { urlPattern = "/api/users" };

        await _ipcClient!.PostAsync("/ipc/breakpoints/rules",
            new StringContent(JsonSerializer.Serialize(rule1), Encoding.UTF8, "application/json"));
        await _ipcClient.PostAsync("/ipc/breakpoints/rules",
            new StringContent(JsonSerializer.Serialize(rule2), Encoding.UTF8, "application/json"));

        var response = await _ipcClient.GetAsync("/ipc/breakpoints/rules");
        var json = await response.Content.ReadAsStringAsync();
        var rules = JsonSerializer.Deserialize<JsonElement>(json);

        Assert.Equal(2, rules.GetArrayLength());
    }

    [Fact]
    public async Task Breakpoints_RemoveRule_ReturnsSuccess()
    {
        // Add a rule first
        var ruleBody = new { method = "DELETE", urlPattern = "/api/items" };
        var content = new StringContent(JsonSerializer.Serialize(ruleBody), Encoding.UTF8, "application/json");
        var addResponse = await _ipcClient!.PostAsync("/ipc/breakpoints/rules", content);
        var addJson = await addResponse.Content.ReadAsStringAsync();
        var addedRule = JsonSerializer.Deserialize<JsonElement>(addJson);
        var ruleId = addedRule.GetProperty("id").GetString()!;

        // Remove it
        var deleteResponse = await _ipcClient.DeleteAsync($"/ipc/breakpoints/rules/{ruleId}");

        Assert.Equal(200, (int)deleteResponse.StatusCode);

        var deleteJson = await deleteResponse.Content.ReadAsStringAsync();
        var deleteResult = JsonSerializer.Deserialize<JsonElement>(deleteJson);
        Assert.True(deleteResult.GetProperty("success").GetBoolean());

        // Verify it's gone
        var listResponse = await _ipcClient.GetAsync("/ipc/breakpoints/rules");
        var listJson = await listResponse.Content.ReadAsStringAsync();
        var rules = JsonSerializer.Deserialize<JsonElement>(listJson);
        Assert.Equal(0, rules.GetArrayLength());
    }

    [Fact]
    public async Task Breakpoints_RemoveNonexistentRule_ReturnsFalse()
    {
        var response = await _ipcClient!.DeleteAsync("/ipc/breakpoints/rules/nonexistent-id");

        Assert.Equal(200, (int)response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(json);

        Assert.False(result.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Breakpoints_ReleaseNonexistentRequest_ReturnsFalse()
    {
        var response = await _ipcClient!.PostAsync("/ipc/breakpoints/nonexistent-id/release", null);

        Assert.Equal(200, (int)response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(json);

        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Contains("not found", result.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Breakpoints_DropNonexistentRequest_ReturnsFalse()
    {
        var response = await _ipcClient!.PostAsync("/ipc/breakpoints/nonexistent-id/drop", null);

        Assert.Equal(200, (int)response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(json);

        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Contains("not found", result.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Breakpoints_FullLifecycle_PauseAndRelease()
    {
        // Add a rule that matches our test request
        var ruleBody = new { urlPattern = "example.com" };
        var ruleContent = new StringContent(JsonSerializer.Serialize(ruleBody), Encoding.UTF8, "application/json");
        await _ipcClient!.PostAsync("/ipc/breakpoints/rules", ruleContent);

        // Pause a request via the hook directly (simulates proxy intercepting a request)
        var correlationId = Guid.NewGuid().ToString();
        var request = new shmoxy.models.dto.InterceptedRequest
        {
            CorrelationId = correlationId,
            Method = "GET",
            Url = new Uri("https://example.com/test")
        };

        // Start the request processing on a background task (it will block waiting for release)
        var hookTask = Task.Run(() => _breakpointHook!.OnRequestAsync(request));

        // Wait briefly for the request to be paused
        await Task.Delay(100);

        // Verify request appears in paused list
        var pausedResponse = await _ipcClient.GetAsync("/ipc/breakpoints/paused");
        var pausedJson = await pausedResponse.Content.ReadAsStringAsync();
        var paused = JsonSerializer.Deserialize<JsonElement>(pausedJson);

        Assert.Equal(1, paused.GetArrayLength());
        var pausedRequest = paused.EnumerateArray().First();
        Assert.Equal(correlationId, pausedRequest.GetProperty("correlationId").GetString());
        Assert.Equal("GET", pausedRequest.GetProperty("method").GetString());
        Assert.Contains("example.com", pausedRequest.GetProperty("url").GetString());

        // Release the request
        var releaseResponse = await _ipcClient.PostAsync($"/ipc/breakpoints/{correlationId}/release", null);
        var releaseJson = await releaseResponse.Content.ReadAsStringAsync();
        var releaseResult = JsonSerializer.Deserialize<JsonElement>(releaseJson);

        Assert.True(releaseResult.GetProperty("success").GetBoolean());

        // Hook task should complete
        var result = await hookTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(result);
        Assert.Equal("GET", result!.Method);

        // Paused list should be empty now
        pausedResponse = await _ipcClient.GetAsync("/ipc/breakpoints/paused");
        pausedJson = await pausedResponse.Content.ReadAsStringAsync();
        paused = JsonSerializer.Deserialize<JsonElement>(pausedJson);
        Assert.Equal(0, paused.GetArrayLength());
    }

    [Fact]
    public async Task Breakpoints_FullLifecycle_PauseAndDrop()
    {
        // Add a rule
        var ruleBody = new { urlPattern = "drop-test.com" };
        var ruleContent = new StringContent(JsonSerializer.Serialize(ruleBody), Encoding.UTF8, "application/json");
        await _ipcClient!.PostAsync("/ipc/breakpoints/rules", ruleContent);

        // Pause a request
        var correlationId = Guid.NewGuid().ToString();
        var request = new shmoxy.models.dto.InterceptedRequest
        {
            CorrelationId = correlationId,
            Method = "POST",
            Url = new Uri("https://drop-test.com/api")
        };

        var hookTask = Task.Run(() => _breakpointHook!.OnRequestAsync(request));
        await Task.Delay(100);

        // Verify it's paused
        var pausedResponse = await _ipcClient.GetAsync("/ipc/breakpoints/paused");
        var pausedJson = await pausedResponse.Content.ReadAsStringAsync();
        var paused = JsonSerializer.Deserialize<JsonElement>(pausedJson);
        Assert.Equal(1, paused.GetArrayLength());

        // Drop the request
        var dropResponse = await _ipcClient.PostAsync($"/ipc/breakpoints/{correlationId}/drop", null);
        var dropJson = await dropResponse.Content.ReadAsStringAsync();
        var dropResult = JsonSerializer.Deserialize<JsonElement>(dropJson);

        Assert.True(dropResult.GetProperty("success").GetBoolean());

        // Hook task should complete with null (dropped)
        var result = await hookTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(result);
    }

    [Fact]
    public async Task Breakpoints_ReleaseWithModifiedRequest()
    {
        // Add a rule
        var ruleBody = new { urlPattern = "modify-test.com" };
        var ruleContent = new StringContent(JsonSerializer.Serialize(ruleBody), Encoding.UTF8, "application/json");
        await _ipcClient!.PostAsync("/ipc/breakpoints/rules", ruleContent);

        // Pause a request
        var correlationId = Guid.NewGuid().ToString();
        var request = new shmoxy.models.dto.InterceptedRequest
        {
            CorrelationId = correlationId,
            Method = "GET",
            Url = new Uri("https://modify-test.com/original")
        };

        var hookTask = Task.Run(() => _breakpointHook!.OnRequestAsync(request));
        await Task.Delay(100);

        // Release with a modified request
        var modified = new { method = "POST", url = "https://modify-test.com/modified" };
        var modifiedContent = new StringContent(JsonSerializer.Serialize(modified), Encoding.UTF8, "application/json");
        var releaseResponse = await _ipcClient!.PostAsync($"/ipc/breakpoints/{correlationId}/release", modifiedContent);
        var releaseJson = await releaseResponse.Content.ReadAsStringAsync();
        var releaseResult = JsonSerializer.Deserialize<JsonElement>(releaseJson);

        Assert.True(releaseResult.GetProperty("success").GetBoolean());

        // Hook task should complete with the modified request
        var result = await hookTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(result);
        Assert.Equal("POST", result!.Method);
    }
}
