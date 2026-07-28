using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using shmoxy.api.server;
using Xunit;

namespace shmoxy.e2e;

/// <summary>
/// The test that was missing.
///
/// Every layer of this stack had unit tests that passed while the running application was
/// useless: the proxy started and forwarded traffic correctly, but the inspection stream
/// never established, so the UI showed nothing and the app looked dead. Nothing in the
/// suite booted the app the way scripts/start.sh does and asserted that traffic actually
/// arrives at a consumer.
///
/// This boots the real API host on a real port, lets it spawn the real proxy binary as a
/// child process, pushes real HTTP through that proxy, and asserts the events come out the
/// other end of the SSE endpoint. If someone breaks the chain anywhere between the proxy
/// engine and the stream consumer, this fails.
/// </summary>
[Trait("Category", "Integration")]
public class ProxyCaptureSmokeTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);
    private const string UpstreamBody = "smoke-test-upstream-payload";

    private WebApplication? _api;
    private IHost? _upstream;
    private HttpClient? _apiClient;
    private string _stateDirectory = string.Empty;
    private string _configDirectory = string.Empty;
    private int _apiPort;
    private int _proxyPort;
    private int _upstreamPort;

    public async Task InitializeAsync()
    {
        _stateDirectory = CreateTempDirectory("state");
        _configDirectory = CreateTempDirectory("config");

        _apiPort = GetFreePort();
        _proxyPort = GetFreePort();
        _upstreamPort = GetFreePort();

        _upstream = await StartUpstreamAsync(_upstreamPort);

        var proxyBinary = Path.Combine(AppContext.BaseDirectory, "shmoxy.dll");
        Assert.True(
            File.Exists(proxyBinary),
            $"the proxy binary should sit beside the test assembly, but {proxyBinary} is missing");

        _api = Program.CreateApp(
            new[]
            {
                // MapStaticAssets derives the manifest filename from the application name,
                // which defaults to the test host rather than the API under test.
                "--applicationName=shmoxy.api",
                $"--urls=http://127.0.0.1:{_apiPort}",
                $"--ApiConfig:ProxyPort={_proxyPort}",
                $"--ApiConfig:ProxyBinaryPath={proxyBinary}",
                $"--ApiConfig:DataDirectory={_stateDirectory}",
                "--ApiConfig:AutoStartProxy=true"
            },
            services =>
            {
                // Keep the test off the developer's real proxy-config.json, which would
                // otherwise dictate the proxy port and make this non-deterministic.
                services.RemoveAll<IConfigPersistence>();
                services.AddSingleton<IConfigPersistence>(sp => new JsonConfigPersistence(
                    sp.GetRequiredService<ILogger<JsonConfigPersistence>>(),
                    _configDirectory));
            });

        await _api.StartAsync();

        _apiClient = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{_apiPort}") };

        await WaitForProxyRunningAsync();
    }

    public async Task DisposeAsync()
    {
        _apiClient?.Dispose();

        if (_api is not null)
        {
            await _api.StopAsync();
            await _api.DisposeAsync();
        }

        if (_upstream is not null)
        {
            await _upstream.StopAsync();
            _upstream.Dispose();
        }

        TryDelete(_stateDirectory);
        TryDelete(_configDirectory);
    }

    /// <summary>
    /// The end-to-end assertion: traffic through the proxy shows up on the inspection
    /// stream. This also pins the idle-stream behaviour that broke the UI -- the stream has
    /// to establish (headers flushed) before any traffic exists, or a consumer with a
    /// request timeout gives up before the first event.
    /// </summary>
    [Fact]
    public async Task TrafficThroughProxy_ReachesInspectionStream()
    {
        using var cts = new CancellationTokenSource(Timeout);

        // Subscribe first, while the proxy is completely idle.
        using var streamClient = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{_apiPort}"),
            Timeout = System.Threading.Timeout.InfiniteTimeSpan
        };

        using var response = await streamClient.GetAsync(
            "/api/proxies/local/inspect/stream",
            HttpCompletionOption.ResponseHeadersRead,
            cts.Token);

        // Headers must arrive on an idle stream. Before the fix this call blocked here
        // until the client's timeout, and the UI never connected.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var events = new List<JsonDocument>();
        var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cts.Token));

        var collector = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cts.Token);
                if (line is null)
                    break;
                if (!line.StartsWith("data: ", StringComparison.Ordinal))
                    continue;

                lock (events)
                    events.Add(JsonDocument.Parse(line["data: ".Length..]));
            }
        }, cts.Token);

        // Now push real traffic through the proxy.
        var proxiedBody = await GetThroughProxyAsync($"http://127.0.0.1:{_upstreamPort}/smoke", cts.Token);

        // The proxy must actually proxy, not just accept the connection.
        Assert.Equal(UpstreamBody, proxiedBody);

        await WaitUntilAsync(
            () =>
            {
                lock (events)
                    return HasEvent(events, "request") && HasEvent(events, "response");
            },
            "a request and a response event to arrive on the inspection stream",
            cts.Token);

        cts.Cancel();
        await IgnoreCancellation(collector);

        lock (events)
        {
            var requestUrls = events
                .Where(e => Value(e, "EventType") == "request")
                .Select(e => Value(e, "Url"))
                .ToList();

            Assert.Contains(requestUrls, url => url is not null && url.Contains($"127.0.0.1:{_upstreamPort}"));
        }
    }

    /// <summary>
    /// The API must write its state into the configured data directory. In Docker that
    /// directory is a mounted volume; if the app writes anywhere else the database is
    /// silently recreated empty on every container restart.
    /// </summary>
    [Fact]
    public void ApiState_LandsInConfiguredDataDirectory()
    {
        Assert.True(
            File.Exists(Path.Combine(_stateDirectory, "proxies.db")),
            $"expected the SQLite database inside {_stateDirectory}");

        Assert.True(
            Directory.Exists(Path.Combine(_stateDirectory, "keys")),
            $"expected the data protection keys inside {_stateDirectory}");
    }

    private async Task<string> GetThroughProxyAsync(string url, CancellationToken ct)
    {
        using var handler = new HttpClientHandler
        {
            Proxy = new WebProxy($"http://127.0.0.1:{_proxyPort}"),
            UseProxy = true
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };

        return await client.GetStringAsync(url, ct);
    }

    private async Task WaitForProxyRunningAsync()
    {
        using var cts = new CancellationTokenSource(Timeout);

        await WaitUntilAsync(
            async () =>
            {
                try
                {
                    var json = await _apiClient!.GetStringAsync("/api/proxies/local", cts.Token);
                    using var document = JsonDocument.Parse(json);
                    return document.RootElement.TryGetProperty("state", out var state) &&
                           state.GetString() == "Running";
                }
                catch (Exception) when (!cts.Token.IsCancellationRequested)
                {
                    return false;
                }
            },
            "the proxy process to report Running",
            cts.Token);
    }

    private static bool HasEvent(List<JsonDocument> events, string eventType) =>
        events.Any(e => Value(e, "EventType") == eventType);

    private static string? Value(JsonDocument document, string property) =>
        document.RootElement.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static Task WaitUntilAsync(Func<bool> condition, string description, CancellationToken ct) =>
        WaitUntilAsync(() => Task.FromResult(condition()), description, ct);

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string description, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (await condition())
                return;

            await Task.Delay(200, ct);
        }

        throw new TimeoutException($"Timed out waiting for {description}");
    }

    private static async Task IgnoreCancellation(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // Expected -- the collector is cancelled once the assertions are satisfied.
        }
    }

    private static async Task<IHost> StartUpstreamAsync(int port)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => logging.ClearProviders())
            .ConfigureWebHostDefaults(web =>
            {
                web.UseKestrel(kestrel => kestrel.ListenLocalhost(port));
                web.Configure(app => app.Run(context =>
                {
                    context.Response.ContentType = "text/plain";
                    return context.Response.WriteAsync(UpstreamBody);
                }));
            })
            .Build();

        await host.StartAsync();
        return host;
    }

    private static string CreateTempDirectory(string suffix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"shmoxy-smoke-{suffix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // A temp directory left behind must not fail the test run.
        }
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
