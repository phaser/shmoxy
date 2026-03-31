using System.Net.Security;
using Microsoft.Extensions.Logging;
using shmoxy.server;
using shmoxy.server.hooks;
using shmoxy.shared.ipc;

namespace shmoxy.tests.server;

public class ProxyServerCertValidationTests : IDisposable
{
    private bool _disposed;

    [Fact]
    public void ValidateCertificate_ReturnsTrue_WhenValidationDisabled()
    {
        // Arrange
        var config = new ProxyConfig
        {
            Port = 0,
            ValidateUpstreamCertificates = false
        };
        using var server = new ProxyServer(config);

        // Act — even with chain errors, should return true when validation is off
        var result = server.ValidateCertificate(
            this,
            certificate: null,
            chain: null,
            sslPolicyErrors: SslPolicyErrors.RemoteCertificateChainErrors);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ValidateCertificate_ReturnsTrue_WhenValidationEnabledAndNoErrors()
    {
        // Arrange
        var config = new ProxyConfig
        {
            Port = 0,
            ValidateUpstreamCertificates = true
        };
        using var server = new ProxyServer(config);

        // Act
        var result = server.ValidateCertificate(
            this,
            certificate: null,
            chain: null,
            sslPolicyErrors: SslPolicyErrors.None);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ValidateCertificate_ReturnsFalse_WhenValidationEnabledAndChainErrors()
    {
        // Arrange
        var config = new ProxyConfig
        {
            Port = 0,
            ValidateUpstreamCertificates = true
        };
        using var server = new ProxyServer(config);

        // Act
        var result = server.ValidateCertificate(
            this,
            certificate: null,
            chain: null,
            sslPolicyErrors: SslPolicyErrors.RemoteCertificateChainErrors);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ValidateCertificate_ReturnsFalse_WhenValidationEnabledAndNameMismatch()
    {
        // Arrange
        var config = new ProxyConfig
        {
            Port = 0,
            ValidateUpstreamCertificates = true
        };
        using var server = new ProxyServer(config);

        // Act
        var result = server.ValidateCertificate(
            this,
            certificate: null,
            chain: null,
            sslPolicyErrors: SslPolicyErrors.RemoteCertificateNameMismatch);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ValidateCertificate_LogsWarning_WhenValidationFails()
    {
        // Arrange
        var logger = new CapturingLogger();
        var config = new ProxyConfig
        {
            Port = 0,
            ValidateUpstreamCertificates = true
        };
        using var server = new ProxyServer(config, new NoOpInterceptHook(), logger);

        // Act
        server.ValidateCertificate(
            this,
            certificate: null,
            chain: null,
            sslPolicyErrors: SslPolicyErrors.RemoteCertificateNameMismatch);

        // Assert — verify a warning was logged
        Assert.Contains(logger.Entries, e => e.LogLevel == LogLevel.Warning
            && e.Message.Contains("certificate validation failed", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }

    /// <summary>
    /// Minimal logger that captures log entries for assertion.
    /// </summary>
    private sealed class CapturingLogger : ILogger<ProxyServer>
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }

        public record LogEntry(LogLevel LogLevel, string Message);
    }
}
