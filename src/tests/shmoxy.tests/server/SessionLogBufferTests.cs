using shmoxy.server;

namespace shmoxy.tests.server;

public class SessionLogBufferTests
{
    [Fact]
    public void Log_WhenDisabled_DoesNotBuffer()
    {
        var buffer = new SessionLogBuffer { Enabled = false };

        buffer.Info("Test", "Should not be buffered");

        var entries = buffer.Drain();
        Assert.Empty(entries);
    }

    [Fact]
    public void Log_WhenEnabled_BuffersEntries()
    {
        var buffer = new SessionLogBuffer { Enabled = true };

        buffer.Info("Detector", "Detector triggered for example.com");
        buffer.Warn("Passthrough", "Temporary passthrough expired");
        buffer.Error("Proxy", "Connection failed");

        var entries = buffer.Drain();
        Assert.Equal(3, entries.Count);
        Assert.Equal("Info", entries[0].Level);
        Assert.Equal("Detector", entries[0].Category);
        Assert.Contains("example.com", entries[0].Message);
        Assert.Equal("Warn", entries[1].Level);
        Assert.Equal("Error", entries[2].Level);
    }

    [Fact]
    public void Drain_ClearsBuffer()
    {
        var buffer = new SessionLogBuffer { Enabled = true };

        buffer.Info("Test", "First entry");
        buffer.Info("Test", "Second entry");

        var first = buffer.Drain();
        Assert.Equal(2, first.Count);

        var second = buffer.Drain();
        Assert.Empty(second);
    }

    [Fact]
    public void Snapshot_DoesNotClearBuffer()
    {
        var buffer = new SessionLogBuffer { Enabled = true };

        buffer.Info("Test", "Entry");

        var snapshot = buffer.Snapshot();
        Assert.Single(snapshot);

        var drain = buffer.Drain();
        Assert.Single(drain);
    }

    [Fact]
    public void Log_SetsTimestamp()
    {
        var buffer = new SessionLogBuffer { Enabled = true };
        var before = DateTime.UtcNow;

        buffer.Info("Test", "Entry");

        var entries = buffer.Drain();
        Assert.Single(entries);
        Assert.True(entries[0].Timestamp >= before);
        Assert.True(entries[0].Timestamp <= DateTime.UtcNow);
    }

    [Fact]
    public void Enabled_CanBeToggled()
    {
        var buffer = new SessionLogBuffer { Enabled = true };

        buffer.Info("Test", "Should be buffered");
        buffer.Enabled = false;
        buffer.Info("Test", "Should not be buffered");
        buffer.Enabled = true;
        buffer.Info("Test", "Should be buffered again");

        var entries = buffer.Drain();
        Assert.Equal(2, entries.Count);
    }
}
