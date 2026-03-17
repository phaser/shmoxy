using Xunit;
using shmoxy;
using System.Security.Cryptography.X509Certificates;

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

    [Fact]
    public void TlsHandler_RootCertificateProperty_ShouldReturnRootCert()
    {
        // Arrange & Act
        using var handler = new TlsHandler();

        // Assert
        Assert.NotNull(handler.RootCertificate);
        // Root cert is self-signed: Issuer == Subject
        Assert.Equal(handler.RootCertificate.Subject, handler.RootCertificate.Issuer);
        Assert.Contains("Shmoxy Proxy CA", handler.RootCertificate.Subject);
    }

    [Fact]
    public void TlsHandler_ExportRootCertificatePem_ShouldReturnValidPem()
    {
        // Arrange & Act
        using var handler = new TlsHandler();
        var pem = handler.ExportRootCertificatePem();

        // Assert
        Assert.NotNull(pem);
        Assert.Contains("-----BEGIN CERTIFICATE-----", pem);
        Assert.Contains("-----END CERTIFICATE-----", pem);
    }

    [Fact]
    public void TlsHandler_ExportRootCertificateDer_ShouldReturnValidBytes()
    {
        // Arrange & Act
        using var handler = new TlsHandler();
        var der = handler.ExportRootCertificateDer();

        // Assert
        Assert.NotNull(der);
        Assert.True(der.Length > 0, "DER bytes should not be empty");
    }

    [Fact]
    public void TlsHandler_PerHostCert_ShouldBeSignedByRootCA()
    {
        // Arrange & Act
        using var handler = new TlsHandler();
        var cert = handler.GetCertificate("example.com");

        // Assert - should be signed by root CA, not self-signed
        // Issuer should contain root CA subject, not the host cert's own subject
        Assert.Contains("Shmoxy Proxy CA", cert.Issuer);
        Assert.NotEqual(cert.Subject, cert.Issuer);
    }
}
