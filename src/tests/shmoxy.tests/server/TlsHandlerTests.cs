using shmoxy.server;

namespace shmoxy.tests.server;

public class TlsHandlerTests
{
    [Fact]
    public void TlsHandler_ShouldCreateRootCertificate()
    {
        // Arrange & Act
        using var handler = new TlsHandler();
        var cert = handler.GetCertificate("example.com");

        // Assert
        Assert.NotNull(cert);
        Assert.True(cert.HasPrivateKey, "Server certificate should have private key for TLS termination");
    }

    [Fact]
    public void TlsHandler_ShouldCacheCertificates()
    {
        // Arrange & Act
        using var handler = new TlsHandler();
        var cert1 = handler.GetCertificate("example.com");
        var cert2 = handler.GetCertificate("example.com");

        // Assert - should return same cached instance
        Assert.Same(cert1, cert2);
    }

    [Fact]
    public void TlsHandler_ShouldGenerateDifferentCertificatesForDifferentHosts()
    {
        // Arrange & Act
        using var handler = new TlsHandler();
        var cert1 = handler.GetCertificate("example.com");
        var cert2 = handler.GetCertificate("other.com");

        // Assert - should be different certificates
        Assert.NotEqual(cert1.Subject, cert2.Subject);
    }

    [Fact]
    public void GetCertificate_NormalizesHostToLowercase()
    {
        using var handler = new TlsHandler();
        var cert1 = handler.GetCertificate("Example.COM");
        var cert2 = handler.GetCertificate("example.com");

        Assert.Same(cert1, cert2);
    }

    [Fact]
    public void GetCertificate_StripsPortFromHostname()
    {
        using var handler = new TlsHandler();
        var cert1 = handler.GetCertificate("example.com:443");
        var cert2 = handler.GetCertificate("example.com");

        Assert.Same(cert1, cert2);
    }

    [Fact]
    public void GetCertificate_ThrowsForNullOrEmpty()
    {
        using var handler = new TlsHandler();

        Assert.Throws<ArgumentException>(() => handler.GetCertificate(""));
        Assert.Throws<ArgumentException>(() => handler.GetCertificate("  "));
    }

    [Fact]
    public void GetRootCertificate_ReturnsCACertificate()
    {
        using var handler = new TlsHandler();
        var root = handler.GetRootCertificate();

        Assert.NotNull(root);
        Assert.Contains("CN=Shmoxy Proxy CA", root.Subject);
    }

    [Fact]
    public void ClearCache_AllowsNewCertificateGeneration()
    {
        using var handler = new TlsHandler();
        var cert1 = handler.GetCertificate("example.com");
        handler.ClearCache();
        var cert2 = handler.GetCertificate("example.com");

        // After clearing, a new certificate should be generated (different instance)
        Assert.NotSame(cert1, cert2);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var handler = new TlsHandler();
        handler.GetCertificate("example.com");

        var exception = Record.Exception(() => handler.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    public void Constructor_WithRootCertificate_UsesProvidedCert()
    {
        using var rootKey = System.Security.Cryptography.RSA.Create(2048);
        var rootReq = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=Test Root CA", rootKey,
            System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        rootReq.CertificateExtensions.Add(
            new System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension(true, false, 0, true));
        var rootCert = rootReq.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1));

        using var handler = new TlsHandler(rootCert);
        var returnedRoot = handler.GetRootCertificate();

        Assert.Same(rootCert, returnedRoot);
    }

    [Fact]
    public void Constructor_WithCertStoragePath_PersistsAndReloads()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"shmoxy-test-{Guid.NewGuid()}");
        try
        {
            using (var handler1 = new TlsHandler(tempDir))
            {
                var root1 = handler1.GetRootCertificate();
                Assert.NotNull(root1);
            }

            // Verify the PFX was saved
            Assert.True(File.Exists(Path.Combine(tempDir, "shmoxy-root-ca.pfx")));
            Assert.True(File.Exists(Path.Combine(tempDir, "shmoxy-root-ca.pem")));

            // Reload from the same path
            using var handler2 = new TlsHandler(tempDir);
            var root2 = handler2.GetRootCertificate();
            Assert.NotNull(root2);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
