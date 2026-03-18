using Microsoft.Playwright;
using shmoxy;
using Xunit;

namespace shmoxy.e2e;

public sealed class ProxyTestFixture : IAsyncLifetime
{
    public IBrowser Browser { get; private set; } = null!;
    public int Port { get; private set; }
    public string BaseUrl => $"http://127.0.0.1:{Port}";
    private IPlaywright? _playwright;
    private ProxyServer? _server;
    private CancellationTokenSource? _cts;

    public async Task InitializeAsync()
    {
        var config = new ProxyConfig { Port = 0, LogLevel = ProxyConfig.LogLevelEnum.Debug };
        
        _server = new ProxyServer(config);
        _cts = new CancellationTokenSource();
        
        // Start server in background
        _ = _server.StartAsync(_cts.Token);
        
        for (int i = 0; i < 20 && !_server.IsListening; i++)
            await Task.Delay(50);

        if (!_server.IsListening)
            throw new InvalidOperationException("Proxy server failed to start");

        Port = _server.ListeningPort;
        Console.WriteLine($"Proxy started on port {Port}");
        
        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new()
        {
            Headless = true,
            Args = new[] { "--no-sandbox", "--disable-setuid-sandbox", "--disable-gpu" },
            Timeout = 30000
        });
        Console.WriteLine("Browser launched");
    }

    public async Task DisposeAsync()
    {
        await Browser.CloseAsync();
        _playwright?.Dispose();
        
        if (_cts != null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
        }
        
        _server?.Dispose();
    }
}
