using shmoxy.server;

namespace shmoxy.tests;

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
}
