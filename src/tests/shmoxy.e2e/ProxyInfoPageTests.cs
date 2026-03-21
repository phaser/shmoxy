using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;
using Xunit;

namespace shmoxy.e2e;

[Trait("Category", "Integration")]
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
        IBrowserContext context = null!;
        try
        {
            context = await _fixture!.CreateContextWithTracingAsync(nameof(InfoPage_ShowsProxyStatus));

            var page = await context.NewPageAsync();
            await page.GotoAsync(_fixture.BaseUrl);

            await Expect(page).ToHaveTitleAsync("Shmoxy Proxy Server");

            var statusText = page.GetByText("Proxy is running");
            await Expect(statusText).ToBeVisibleAsync();

            var serverInfo = page.GetByText("Server Information");
            await Expect(serverInfo).ToBeVisibleAsync();

            var listeningInfo = page.GetByText($"http://localhost:{_fixture.Port}");
            await Expect(listeningInfo).ToBeVisibleAsync();
            
            await _fixture.SaveTracingAsync(context, nameof(InfoPage_ShowsProxyStatus), success: true);
        }
        catch
        {
            if (context != null)
            {
                await _fixture!.SaveTracingAsync(context, nameof(InfoPage_ShowsProxyStatus), success: false);
            }
            throw;
        }
    }

    [Fact]
    public async Task InfoPage_ContainsUsageInstructions()
    {
        IBrowserContext context = null!;
        try
        {
            context = await _fixture!.CreateContextWithTracingAsync(nameof(InfoPage_ContainsUsageInstructions));

            var page = await context.NewPageAsync();
            await page.GotoAsync(_fixture.BaseUrl);

            var howToUse = page.GetByText("How to use");
            await Expect(howToUse).ToBeVisibleAsync();

            var httpProxyLabel = page.GetByText("HTTP Proxy:");
            await Expect(httpProxyLabel).ToBeVisibleAsync();
            
            await _fixture.SaveTracingAsync(context, nameof(InfoPage_ContainsUsageInstructions), success: true);
        }
        catch
        {
            if (context != null)
            {
                await _fixture!.SaveTracingAsync(context, nameof(InfoPage_ContainsUsageInstructions), success: false);
            }
            throw;
        }
    }

    [Fact]
    public async Task InfoPage_ReturnsHtmlContentType()
    {
        IBrowserContext context = null!;
        try
        {
            context = await _fixture!.CreateContextWithTracingAsync(nameof(InfoPage_ReturnsHtmlContentType));

            var page = await context.NewPageAsync();
            var response = await page.GotoAsync(_fixture.BaseUrl);

            Assert.NotNull(response);
            Assert.Equal(200, response.Status);
            
            var contentType = response.Headers["content-type"];
            Assert.Contains("text/html", contentType);
            
            await _fixture.SaveTracingAsync(context, nameof(InfoPage_ReturnsHtmlContentType), success: true);
        }
        catch
        {
            if (context != null)
            {
                await _fixture!.SaveTracingAsync(context, nameof(InfoPage_ReturnsHtmlContentType), success: false);
            }
            throw;
        }
    }

    [Fact]
    public async Task InfoPage_HasCertificateDownloadLinks()
    {
        IBrowserContext context = null!;
        try
        {
            context = await _fixture!.CreateContextWithTracingAsync(nameof(InfoPage_HasCertificateDownloadLinks));

            var page = await context.NewPageAsync();
            await page.GotoAsync(_fixture.BaseUrl);

            var pemLink = page.GetByRole(AriaRole.Link, new() { Name = "Download PEM" });
            await Expect(pemLink).ToBeVisibleAsync();
            
            var derLink = page.GetByRole(AriaRole.Link, new() { Name = "Download DER" });
            await Expect(derLink).ToBeVisibleAsync();
            
            await _fixture.SaveTracingAsync(context, nameof(InfoPage_HasCertificateDownloadLinks), success: true);
        }
        catch
        {
            if (context != null)
            {
                await _fixture!.SaveTracingAsync(context, nameof(InfoPage_HasCertificateDownloadLinks), success: false);
            }
            throw;
        }
    }

    [Fact]
    public async Task RootCaPem_Endpoint_ReturnsCertificate()
    {
        using var client = new System.Net.Http.HttpClient();
        var response = await client.GetAsync($"{_fixture!.BaseUrl}/root-ca.pem");

        Assert.NotNull(response);
        Assert.Equal(200, (int)response.StatusCode);
        Assert.Contains("pem", response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant());
        
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("-----BEGIN CERTIFICATE-----", content);
        Assert.Contains("-----END CERTIFICATE-----", content);
    }
}
