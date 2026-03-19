using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Net.Sockets;

namespace shmoxy.server;

/// <summary>
/// Handles TLS termination and dynamic certificate generation.
/// Supports SNI (Server Name Indication) for multi-domain proxying.
/// </summary>
public class TlsHandler : IDisposable
{
    private readonly X509Certificate2 _rootCert;
    private readonly ConcurrentDictionary<string, X509Certificate2> _certCache = new();
    private bool _disposed;

    /// <summary>
    /// Creates a TLS handler with dynamic certificate generation.
    /// </summary>
    public TlsHandler()
    {
        _rootCert = GenerateRootCertificate();
    }

    /// <summary>
    /// Creates a TLS handler with a provided root certificate.
    /// </summary>
    public TlsHandler(X509Certificate2 rootCertificate)
    {
        if (rootCertificate == null) throw new ArgumentNullException(nameof(rootCertificate));
        _rootCert = rootCertificate;
    }

    /// <summary>
    /// Gets the root CA certificate.
    /// </summary>
    public X509Certificate2 GetRootCertificate()
    {
        return _rootCert;
    }

    /// <summary>
    /// Gets or generates a certificate for the specified hostname.
    /// Uses SNI to determine which certificate to serve.
    /// </summary>
    public X509Certificate2 GetCertificate(string serverName)
    {
        if (string.IsNullOrWhiteSpace(serverName))
            throw new ArgumentException("Server name is required", nameof(serverName));

        var normalizedName = serverName.ToLowerInvariant().Split(':')[0];

        return _certCache.GetOrAdd(normalizedName, host => GenerateCertificateForHost(host));
    }

    /// <summary>
    /// Generates a self-signed root certificate for signing other certificates.
    /// </summary>
    private X509Certificate2 GenerateRootCertificate()
    {
        var now = DateTime.UtcNow;
        var serialNumber = BitConverter.ToString(server.helpers.RNGCryptoServiceProvider.GetRandomBytes(8)).Replace("-", "");

        using var privateKey = RSA.Create(2048);
        var request = new CertificateRequest("CN=Shmoxy Proxy CA,O=Shmoxy,C=US", privateKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        // Self-signed root certificate valid for 10 years
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));

        var cert = request.CreateSelfSigned(now, now.AddYears(10));
        return cert;
    }

    /// <summary>
    /// Generates a certificate for the specified host using the root CA.
    /// </summary>
    private X509Certificate2 GenerateCertificateForHost(string hostName)
    {
        var now = DateTime.UtcNow;
        var serialNumberBytes = server.helpers.RNGCryptoServiceProvider.GetRandomBytes(8);

        using var privateKey = RSA.Create(2048);
        var request = new CertificateRequest($"CN={hostName},O=Shmoxy,C=US", privateKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        // Add extensions for server authentication with SNI support
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, // TLS Web Server Authentication
            false));
        
        // Add Subject Alternative Name (SAN) extension - required by modern browsers
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(hostName);
        request.CertificateExtensions.Add(sanBuilder.Build());

        // Sign with root CA instead of self-signing
        var cert = request.Create(_rootCert, now, now.AddYears(1), serialNumberBytes);
        
        // Attach the private key to the generated certificate for TLS termination
        var certWithKey = cert.CopyWithPrivateKey(privateKey);
        return certWithKey;
    }

    /// <summary>
    /// Clears the certificate cache.
    /// </summary>
    public void ClearCache()
    {
        foreach (var cert in _certCache.Values)
            cert.Dispose();
        _certCache.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;

        ClearCache();
        _rootCert?.Dispose();
        _disposed = true;
    }
}
