using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using shmoxy.shared.ipc;

namespace shmoxy.server.helpers;

/// <summary>
/// Loads and manages client certificates for mTLS connections to upstream servers.
/// Matches host patterns using the same glob convention as <see cref="HostMatcher"/>.
/// </summary>
public sealed class ClientCertificateProvider : IDisposable
{
    private readonly List<(string pattern, X509Certificate2 cert)> _entries = new();
    private readonly ILogger _logger;

    public ClientCertificateProvider(List<ClientCertConfig> configs, ILogger logger)
    {
        _logger = logger;
        foreach (var config in configs)
        {
            try
            {
                var cert = X509CertificateLoader.LoadPkcs12FromFile(
                    config.CertPath, config.Password);

                _entries.Add((config.HostPattern, cert));
                logger.LogInformation(
                    "Loaded client certificate for {Pattern}: {Subject}",
                    config.HostPattern, cert.Subject);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to load client certificate from {CertPath} for {Pattern}",
                    config.CertPath, config.HostPattern);
            }
        }
    }

    /// <summary>
    /// Returns the first client certificate whose host pattern matches the given host,
    /// or null if no pattern matches.
    /// </summary>
    public X509Certificate2? GetCertificateForHost(string host)
    {
        foreach (var (pattern, cert) in _entries)
        {
            if (HostMatcher.IsMatch(host, pattern))
                return cert;
        }
        return null;
    }

    public void Dispose()
    {
        foreach (var (_, cert) in _entries)
            cert.Dispose();
    }
}
