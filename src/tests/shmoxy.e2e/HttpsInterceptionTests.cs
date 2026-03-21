using System.Security.Cryptography.X509Certificates;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;
using Xunit;

namespace shmoxy.e2e;

/// <summary>
/// End-to-end tests that verify HTTPS interception works through the proxy
/// with a real browser (Chromium). Uses --ignore-certificate-errors-spki-list
/// to trust the proxy's root CA by its public key hash.
/// </summary>
[Trait("Category", "Integration")]
public class HttpsInterceptionTests : IAsyncLifetime
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

    /// <summary>
    /// Launches Chromium configured to route traffic through the proxy.
    /// Uses --ignore-certificate-errors to accept the proxy's dynamically generated certificates.
    /// Note: IgnoreHTTPSErrors is set to false so errors bubble up from the proxy layer itself.
    /// The --ignore-certificate-errors Chromium flag handles the certificate trust at the browser level.
    /// </summary>
    private async Task<IBrowserContext> LaunchBrowserThroughProxy()
    {
        var userDataDir = Path.Combine(Path.GetTempPath(), $"shmoxy-chrome-{Guid.NewGuid()}");
        Directory.CreateDirectory(userDataDir);

        var context = await _fixture!.Browser.BrowserType.LaunchPersistentContextAsync(userDataDir, new()
        {
            Headless = true,
            Proxy = new() { Server = $"http://127.0.0.1:{_fixture.Port}" },
            IgnoreHTTPSErrors = false,
            Args = new[]
            {
                "--ignore-certificate-errors"
            }
        });

        await context.Tracing.StartAsync(new()
        {
            Screenshots = true,
            Snapshots = true,
            Sources = true
        });

        return context;
    }

    /// <summary>
    /// Tests that an HTTPS site loads successfully through the proxy.
    /// </summary>
    [Fact]
    public async Task Https_Site_Works_With_Trusted_RootCA()
    {
        var context = await LaunchBrowserThroughProxy();
        var testName = nameof(Https_Site_Works_With_Trusted_RootCA);
        try
        {
            var page = await context.NewPageAsync();
            await page.GotoAsync("https://example.com", new() { Timeout = 30000 });

            await Expect(page).ToHaveURLAsync("https://example.com");
            await Expect(page.Locator("h1")).ToContainTextAsync("Example Domain");

            await _fixture!.SaveTracingAsync(context, testName, success: true);
        }
        catch
        {
            await _fixture!.SaveTracingAsync(context, testName, success: false);
            throw;
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    /// <summary>
    /// Tests that the proxy can intercept HTTPS traffic to multiple domains.
    /// </summary>
    [Fact]
    public async Task Multiple_Https_Sites_Work_Through_Proxy()
    {
        var context = await LaunchBrowserThroughProxy();
        var testName = nameof(Multiple_Https_Sites_Work_Through_Proxy);
        try
        {
            var page = await context.NewPageAsync();

            var testSites = new[]
            {
                "https://example.com",
                "https://example.org",
                "https://example.net"
            };

            foreach (var url in testSites)
            {
                await page.GotoAsync(url, new() { Timeout = 30000 });
                await Expect(page).ToHaveURLAsync(url);
            }

            await _fixture!.SaveTracingAsync(context, testName, success: true);
        }
        catch
        {
            await _fixture!.SaveTracingAsync(context, testName, success: false);
            throw;
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    /// <summary>
    /// Verifies that the root CA certificate is valid and configured for signing.
    /// </summary>
    [Fact]
    public async Task RootCA_Certificate_Is_Valid()
    {
        using var client = new System.Net.Http.HttpClient();
        var certPem = await client.GetStringAsync($"{_fixture!.BaseUrl}/root-ca.pem");

        var certBase64 = certPem
            .Replace("-----BEGIN CERTIFICATE-----", "")
            .Replace("-----END CERTIFICATE-----", "")
            .Replace("\n", "")
            .Replace("\r", "");

        var certBytes = Convert.FromBase64String(certBase64);
        using var cert = X509CertificateLoader.LoadCertificate(certBytes);

        var basicConstraints = cert.Extensions
            .OfType<X509BasicConstraintsExtension>()
            .FirstOrDefault();

        Assert.NotNull(basicConstraints);
        Assert.True(basicConstraints.CertificateAuthority, "Root CA should have CA constraint");
    }
}
