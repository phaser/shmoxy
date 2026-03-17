using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace shmoxy;

/// <summary>
/// Handles TLS termination and dynamic certificate generation.
/// Supports SNI (Server Name Indication) for multi-domain proxying.
/// </summary>
public class TlsHandler : IDisposable
{
    private readonly X509Certificate2 _rootCert;
    private readonly Dictionary<string, X509Certificate2> _certCache = new();
    private readonly object _cacheLock = new();
    private bool _disposed;

    /// <summary>
    /// Gets the root CA certificate used to sign per-host certificates.
    /// </summary>
    public X509Certificate2 RootCertificate => _rootCert;

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
    /// Gets or generates a certificate for the specified hostname.
    /// Uses SNI to determine which certificate to serve.
    /// </summary>
    public X509Certificate2 GetCertificate(string serverName)
    {
        if (string.IsNullOrWhiteSpace(serverName))
            throw new ArgumentException("Server name is required", nameof(serverName));

        // Normalize hostname (remove port, lowercase)
        var normalizedName = serverName.ToLowerInvariant().Split(':')[0];

        lock (_cacheLock)
        {
            if (_certCache.TryGetValue(normalizedName, out var cachedCert))
                return cachedCert;
        }

        var cert = GenerateCertificateForHost(normalizedName);

        lock (_cacheLock)
        {
            _certCache[normalizedName] = cert;
        }

        return cert;
    }

    /// <summary>
    /// Generates a self-signed root certificate for signing other certificates.
    /// </summary>
    private X509Certificate2 GenerateRootCertificate()
    {
        var now = DateTime.UtcNow;

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

        using var privateKey = RSA.Create(2048);
        var request = new CertificateRequest($"CN={hostName},O=Shmoxy,C=US", privateKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        // Add Subject Alternative Name for browser compatibility
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(hostName);
        request.CertificateExtensions.Add(sanBuilder.Build());

        // Add extensions for server authentication
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));

        // Sign with the root CA instead of self-signing
        var serialNumber = RandomNumberGenerator.GetBytes(8);
        using var issuedCert = request.Create(_rootCert, now, now.AddYears(1), serialNumber);
        return issuedCert.CopyWithPrivateKey(privateKey);
    }

    /// <summary>
    /// Clears the certificate cache.
    /// </summary>
    public void ClearCache()
    {
        lock (_cacheLock)
        {
            foreach (var cert in _certCache.Values)
                cert.Dispose();
            _certCache.Clear();
        }
    }

    /// <summary>
    /// Exports the root CA certificate as a PEM-encoded string.
    /// </summary>
    public string ExportRootCertificatePem()
    {
        var certBase64 = Convert.ToBase64String(_rootCert.Export(X509ContentType.Cert));
        var sb = new System.Text.StringBuilder();
        sb.Append("-----BEGIN CERTIFICATE-----\r\n");
        // Wrap at 64 characters per line as per PEM spec
        for (int i = 0; i < certBase64.Length; i += 64)
        {
            sb.Append(certBase64.AsSpan(i, Math.Min(64, certBase64.Length - i)));
            sb.Append("\r\n");
        }
        sb.Append("-----END CERTIFICATE-----\r\n");
        return sb.ToString();
    }

    /// <summary>
    /// Exports the root CA certificate as DER-encoded bytes.
    /// </summary>
    public byte[] ExportRootCertificateDer()
    {
        return _rootCert.Export(X509ContentType.Cert);
    }

    public void Dispose()
    {
        if (_disposed) return;

        ClearCache();
        _rootCert?.Dispose();
        _disposed = true;
    }
}
