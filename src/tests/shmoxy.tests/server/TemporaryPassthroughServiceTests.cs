using shmoxy.server;

namespace shmoxy.tests.server;

public class TemporaryPassthroughServiceTests : IDisposable
{
    private readonly TemporaryPassthroughService _service;

    public TemporaryPassthroughServiceTests()
    {
        _service = new TemporaryPassthroughService(maxConnections: 2, timeout: TimeSpan.FromSeconds(30));
    }

    public void Dispose()
    {
        _service.Dispose();
    }

    [Fact]
    public void Activate_ThenShouldPassthrough_ReturnsTrue()
    {
        _service.Activate("example.com", "cloudflare", "TLS fingerprint rejection");

        Assert.True(_service.ShouldPassthrough("example.com"));
    }

    [Fact]
    public void ShouldPassthrough_ReturnsFalse_ForUnknownHost()
    {
        Assert.False(_service.ShouldPassthrough("unknown.com"));
    }

    [Fact]
    public void RecordConnection_DecrementsCounter()
    {
        _service.Activate("example.com", "cloudflare", "TLS fingerprint rejection");

        _service.RecordConnection("example.com");

        var entries = _service.GetActiveEntries();
        Assert.Single(entries);
        Assert.Equal(1, entries[0].RemainingConnections);
    }

    [Fact]
    public void ShouldPassthrough_ReturnsFalse_AfterMaxConnections()
    {
        _service.Activate("example.com", "cloudflare", "TLS fingerprint rejection");

        _service.RecordConnection("example.com");
        _service.RecordConnection("example.com");

        Assert.False(_service.ShouldPassthrough("example.com"));
    }

    [Fact]
    public void ShouldPassthrough_ReturnsFalse_AfterTimeout()
    {
        using var service = new TemporaryPassthroughService(maxConnections: 10, timeout: TimeSpan.Zero);
        service.Activate("example.com", "cloudflare", "TLS fingerprint rejection");

        // Timeout is zero, so it should already be expired
        Assert.False(service.ShouldPassthrough("example.com"));
    }

    [Fact]
    public void Activate_SameHostTwice_ResetsWindow()
    {
        _service.Activate("example.com", "cloudflare", "TLS fingerprint rejection");
        _service.RecordConnection("example.com");

        // Re-activate resets the counter
        _service.Activate("example.com", "cloudflare", "TLS fingerprint rejection");

        var entries = _service.GetActiveEntries();
        Assert.Single(entries);
        Assert.Equal(2, entries[0].RemainingConnections);
    }

    [Fact]
    public void GetActiveEntries_ReturnsOnlyActive()
    {
        _service.Activate("active.com", "cloudflare", "reason1");
        _service.Activate("expired.com", "waf", "reason2");

        // Exhaust connections for expired.com
        _service.RecordConnection("expired.com");
        _service.RecordConnection("expired.com");

        var entries = _service.GetActiveEntries();
        Assert.Single(entries);
        Assert.Equal("active.com", entries[0].Host);
    }

    [Fact]
    public void GetActiveEntries_ReturnsCorrectFields()
    {
        _service.Activate("example.com", "oauth", "OAuth token endpoint failed");

        var entries = _service.GetActiveEntries();
        var entry = Assert.Single(entries);
        Assert.Equal("example.com", entry.Host);
        Assert.Equal("oauth", entry.DetectorId);
        Assert.Equal("OAuth token endpoint failed", entry.Reason);
        Assert.Equal(2, entry.RemainingConnections);
        Assert.Equal(2, entry.MaxConnections);
    }

    [Fact]
    public void OnActivated_FiresWhenActivated()
    {
        string? activatedHost = null;
        _service.OnActivated += entry => activatedHost = entry.Host;

        _service.Activate("example.com", "cloudflare", "reason");

        Assert.Equal("example.com", activatedHost);
    }

    [Fact]
    public void OnExpired_FiresWhenConnectionsExhausted()
    {
        string? expiredHost = null;
        _service.OnExpired += host => expiredHost = host;

        _service.Activate("example.com", "cloudflare", "reason");
        _service.RecordConnection("example.com");
        _service.RecordConnection("example.com");

        Assert.Equal("example.com", expiredHost);
    }

    [Fact]
    public void RecordConnection_NoOpForUnknownHost()
    {
        // Should not throw
        _service.RecordConnection("unknown.com");
    }
}
