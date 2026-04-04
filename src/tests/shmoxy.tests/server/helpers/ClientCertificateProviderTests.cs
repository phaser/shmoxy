using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using shmoxy.server.helpers;
using shmoxy.shared.ipc;

namespace shmoxy.tests.server.helpers;

public class ClientCertificateProviderTests : IDisposable
{
    private readonly List<string> _tempFiles = new();
    private readonly ILogger _logger = NullLogger.Instance;

    private string CreateTestPfx(string subjectName = "CN=Test", string? password = null)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(subjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1));
        var pfxBytes = password != null
            ? cert.Export(X509ContentType.Pfx, password)
            : cert.Export(X509ContentType.Pfx);
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, pfxBytes);
        _tempFiles.Add(path);
        return path;
    }

    [Fact]
    public void GetCertificateForHost_ExactMatch_ReturnsCert()
    {
        var pfxPath = CreateTestPfx("CN=api.example.com");
        var configs = new List<ClientCertConfig>
        {
            new() { HostPattern = "api.example.com", CertPath = pfxPath }
        };

        using var provider = new ClientCertificateProvider(configs, _logger);

        var cert = provider.GetCertificateForHost("api.example.com");
        Assert.NotNull(cert);
        Assert.Contains("api.example.com", cert.Subject);
    }

    [Fact]
    public void GetCertificateForHost_ExactMatch_CaseInsensitive()
    {
        var pfxPath = CreateTestPfx("CN=api.example.com");
        var configs = new List<ClientCertConfig>
        {
            new() { HostPattern = "api.example.com", CertPath = pfxPath }
        };

        using var provider = new ClientCertificateProvider(configs, _logger);

        Assert.NotNull(provider.GetCertificateForHost("API.EXAMPLE.COM"));
    }

    [Fact]
    public void GetCertificateForHost_GlobMatch_ReturnsCert()
    {
        var pfxPath = CreateTestPfx("CN=Internal Wildcard");
        var configs = new List<ClientCertConfig>
        {
            new() { HostPattern = "*.internal.corp", CertPath = pfxPath }
        };

        using var provider = new ClientCertificateProvider(configs, _logger);

        Assert.NotNull(provider.GetCertificateForHost("api.internal.corp"));
        Assert.NotNull(provider.GetCertificateForHost("db.internal.corp"));
    }

    [Fact]
    public void GetCertificateForHost_GlobMatch_DoesNotMatchExactDomain()
    {
        var pfxPath = CreateTestPfx("CN=Wildcard");
        var configs = new List<ClientCertConfig>
        {
            new() { HostPattern = "*.internal.corp", CertPath = pfxPath }
        };

        using var provider = new ClientCertificateProvider(configs, _logger);

        Assert.Null(provider.GetCertificateForHost("internal.corp"));
    }

    [Fact]
    public void GetCertificateForHost_NoMatch_ReturnsNull()
    {
        var pfxPath = CreateTestPfx("CN=api.example.com");
        var configs = new List<ClientCertConfig>
        {
            new() { HostPattern = "api.example.com", CertPath = pfxPath }
        };

        using var provider = new ClientCertificateProvider(configs, _logger);

        Assert.Null(provider.GetCertificateForHost("other.example.com"));
    }

    [Fact]
    public void GetCertificateForHost_MultipleEntries_ReturnsFirstMatch()
    {
        var pfxExact = CreateTestPfx("CN=Exact");
        var pfxWild = CreateTestPfx("CN=Wildcard");
        var configs = new List<ClientCertConfig>
        {
            new() { HostPattern = "api.example.com", CertPath = pfxExact },
            new() { HostPattern = "*.example.com", CertPath = pfxWild }
        };

        using var provider = new ClientCertificateProvider(configs, _logger);

        // Exact match should win because it appears first
        var cert = provider.GetCertificateForHost("api.example.com");
        Assert.NotNull(cert);
        Assert.Contains("Exact", cert.Subject);

        // Wildcard should match other subdomains
        var wildCert = provider.GetCertificateForHost("other.example.com");
        Assert.NotNull(wildCert);
        Assert.Contains("Wildcard", wildCert.Subject);
    }

    [Fact]
    public void Constructor_InvalidCertPath_LogsWarningAndContinues()
    {
        var validPfx = CreateTestPfx("CN=Valid");
        var configs = new List<ClientCertConfig>
        {
            new() { HostPattern = "bad.example.com", CertPath = "/nonexistent/path.pfx" },
            new() { HostPattern = "good.example.com", CertPath = validPfx }
        };

        using var provider = new ClientCertificateProvider(configs, _logger);

        // Invalid cert should be skipped; valid cert should still work
        Assert.Null(provider.GetCertificateForHost("bad.example.com"));
        Assert.NotNull(provider.GetCertificateForHost("good.example.com"));
    }

    [Fact]
    public void Constructor_WithPassword_LoadsCert()
    {
        var pfxPath = CreateTestPfx("CN=Protected", password: "test123");
        var configs = new List<ClientCertConfig>
        {
            new() { HostPattern = "secure.example.com", CertPath = pfxPath, Password = "test123" }
        };

        using var provider = new ClientCertificateProvider(configs, _logger);

        Assert.NotNull(provider.GetCertificateForHost("secure.example.com"));
    }

    [Fact]
    public void GetCertificateForHost_EmptyConfigs_ReturnsNull()
    {
        using var provider = new ClientCertificateProvider(new List<ClientCertConfig>(), _logger);

        Assert.Null(provider.GetCertificateForHost("any.host.com"));
    }

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            try { File.Delete(path); }
            catch { /* best-effort cleanup */ }
        }
    }
}
