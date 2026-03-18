using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;
using Xunit;

namespace shmoxy.e2e;

public class ProxyInfoPageTests : IAsyncLifetime
{
    private ProxyTestFixture? _fixture;

    public async Task InitializeAsync()
    {
        _fixture = new ProxyTestFixture();
        await _fixture.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        if (_fixture != null)
        {
            await _fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task InfoPage_ShowsProxyStatus()
    {
        var context = await _fixture!.Browser.NewContextAsync(new()
        {
            IgnoreHTTPSErrors = true
        });

        var page = await context.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);

        await Expect(page).ToHaveTitleAsync("Shmoxy Proxy Server");

        var statusText = page.GetByText("Proxy is running");
        await Expect(statusText).ToBeVisibleAsync();

        var serverInfo = page.GetByText("Server Information");
        await Expect(serverInfo).ToBeVisibleAsync();

        var listeningInfo = page.GetByText($"http://localhost:{_fixture.Port}");
        await Expect(listeningInfo).ToBeVisibleAsync();
    }

    [Fact]
    public async Task InfoPage_ContainsUsageInstructions()
    {
        var context = await _fixture!.Browser.NewContextAsync(new()
        {
            IgnoreHTTPSErrors = true
        });

        var page = await context.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);

        var howToUse = page.GetByText("How to use");
        await Expect(howToUse).ToBeVisibleAsync();

        var httpProxyLabel = page.GetByText("HTTP Proxy:");
        await Expect(httpProxyLabel).ToBeVisibleAsync();
    }

    [Fact]
    public async Task InfoPage_ReturnsHtmlContentType()
    {
        var context = await _fixture!.Browser.NewContextAsync(new()
        {
            IgnoreHTTPSErrors = true
        });

        var page = await context.NewPageAsync();
        var response = await page.GotoAsync(_fixture.BaseUrl);

        Assert.NotNull(response);
        Assert.Equal(200, response.Status);
        
        var contentType = response.Headers["content-type"];
        Assert.Contains("text/html", contentType);
    }

    [Fact]
    public async Task InfoPage_HasCertificateDownloadLinks()
    {
        var context = await _fixture!.Browser.NewContextAsync(new()
        {
            IgnoreHTTPSErrors = true
        });

        var page = await context.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);

        var pemLink = page.GetByRole(AriaRole.Link, new() { Name = "Download PEM" });
        await Expect(pemLink).ToBeVisibleAsync();
        
        var derLink = page.GetByRole(AriaRole.Link, new() { Name = "Download DER" });
        await Expect(derLink).ToBeVisibleAsync();
    }

    [Fact]
    public async Task RootCaPem_Endpoint_ReturnsCertificate()
    {
        using var client = new System.Net.Http.HttpClient();
        var response = await client.GetAsync($"{_fixture.BaseUrl}/root-ca.pem");

        Assert.Equal(200, (int)response.StatusCode);
        Assert.Contains("pem", response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant());
        
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("-----BEGIN CERTIFICATE-----", content);
        Assert.Contains("-----END CERTIFICATE-----", content);
    }
}
