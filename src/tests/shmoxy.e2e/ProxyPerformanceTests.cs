using System.Diagnostics;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;
using shmoxy.server;

namespace shmoxy.e2e;

/// <summary>
/// Performance benchmarks for the proxy server.
/// Measures total page load time for real-world sites with many parallel resources.
/// </summary>
[Trait("Category", "Performance")]
public class ProxyPerformanceTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private ProxyTestFixture? _fixture;

    /// <summary>
    /// Real-world sites that load many resources in parallel.
    /// These will expose the single-threaded bottleneck.
    /// </summary>
    private static readonly string[] TestSites = new[]
    {
        "https://finance.yahoo.com",
        "https://www.reddit.com",
        "https://arstechnica.com"
    };

    public ProxyPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        _fixture = new ProxyTestFixture();
        await _fixture.InitializeAsync();
        _output.WriteLine($"Proxy started on port {_fixture.Port}");
    }

    public async Task DisposeAsync()
    {
        if (_fixture != null)
        {
            await _fixture.DisposeAsync();
        }
    }

    /// <summary>
    /// Measures baseline: total time to load all test sites WITHOUT proxy.
    /// Sites are loaded sequentially, one after another.
    /// Record this baseline - the proxy overhead should be minimal.
    /// </summary>
    [Fact]
    public async Task Baseline_NoProxy_TotalLoadTime()
    {
        var testName = nameof(Baseline_NoProxy_TotalLoadTime);
        var context = await _fixture!.CreateContextWithTracingAsync(testName, useProxy: false);
        var page = await context.NewPageAsync();

        try
        {
            var sw = Stopwatch.StartNew();
            
            foreach (var url in TestSites)
            {
                _output.WriteLine($"Loading (no proxy): {url}");
                var pageSw = Stopwatch.StartNew();
                await page.GotoAsync(url, new() { Timeout = 60000, WaitUntil = WaitUntilState.Load });
                pageSw.Stop();
                _output.WriteLine($"  -> {pageSw.ElapsedMilliseconds}ms");
            }
            
            sw.Stop();
            var totalTime = sw.ElapsedMilliseconds;

            _output.WriteLine($"BASELINE (no proxy): {totalTime}ms total for {TestSites.Length} sites");
            
            // Save baseline to file for future comparison
            var baselinePath = Path.Combine(_fixture.ArtifactsDir, "baseline_no_proxy.txt");
            await File.WriteAllTextAsync(baselinePath, $"{totalTime}");
            _output.WriteLine($"Baseline saved to: {baselinePath}");

            await _fixture.SaveTracingAsync(context, testName, success: true);
        }
        catch
        {
            await _fixture.SaveTracingAsync(context, testName, success: false);
            throw;
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    /// <summary>
    /// Measures proxy overhead: total time to load all test sites THROUGH proxy.
    /// Sites are loaded sequentially, one after another.
    /// Compare this to the baseline to see the proxy overhead.
    /// </summary>
    [Fact]
    public async Task Proxy_WithProxy_TotalLoadTime()
    {
        var testName = nameof(Proxy_WithProxy_TotalLoadTime);
        var context = await _fixture!.CreateContextWithTracingAsync(testName, useProxy: true);
        var page = await context.NewPageAsync();

        try
        {
            var sw = Stopwatch.StartNew();
            
            foreach (var url in TestSites)
            {
                _output.WriteLine($"Loading (with proxy): {url}");
                var pageSw = Stopwatch.StartNew();
                await page.GotoAsync(url, new() { Timeout = 60000, WaitUntil = WaitUntilState.Load });
                pageSw.Stop();
                _output.WriteLine($"  -> {pageSw.ElapsedMilliseconds}ms");
            }
            
            sw.Stop();
            var totalTime = sw.ElapsedMilliseconds;

            _output.WriteLine($"PROXY (with proxy): {totalTime}ms total for {TestSites.Length} sites");

            // Try to read baseline for comparison
            try
            {
                var baselinePath = Path.Combine(_fixture.ArtifactsDir, "baseline_no_proxy.txt");
                if (File.Exists(baselinePath))
                {
                    var baseline = long.Parse(await File.ReadAllTextAsync(baselinePath));
                    var overhead = totalTime - baseline;
                    var overheadPercent = (overhead * 100.0) / baseline;
                    _output.WriteLine($"BASELINE: {baseline}ms");
                    _output.WriteLine($"OVERHEAD: {overhead}ms ({overheadPercent:F1}%)");
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Could not compare to baseline: {ex.Message}");
            }

            await _fixture.SaveTracingAsync(context, testName, success: true);
        }
        catch
        {
            await _fixture.SaveTracingAsync(context, testName, success: false);
            throw;
        }
        finally
        {
            await context.CloseAsync();
        }
    }
}
