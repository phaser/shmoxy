using Microsoft.Extensions.Logging;
using shmoxy.server;

namespace shmoxy.tests.server;

public class SessionLogBufferProviderTests
{
    [Fact]
    public void CreateLogger_ReturnsNonNullLogger()
    {
        var buffer = new SessionLogBuffer { Enabled = true };
        var provider = new SessionLogBufferProvider(buffer);

        var logger = provider.CreateLogger("TestCategory");

        Assert.NotNull(logger);
    }

    [Fact]
    public void Logger_WhenBufferEnabled_LogsInformationLevel()
    {
        var buffer = new SessionLogBuffer { Enabled = true };
        var provider = new SessionLogBufferProvider(buffer);
        var logger = provider.CreateLogger("TestCategory");

        logger.LogInformation("Test message");

        var entries = buffer.Drain();
        Assert.Single(entries);
        Assert.Equal("Info", entries[0].Level);
        Assert.Equal("TestCategory", entries[0].Category);
        Assert.Equal("Test message", entries[0].Message);
    }

    [Fact]
    public void Logger_WhenBufferEnabled_LogsWarningLevel()
    {
        var buffer = new SessionLogBuffer { Enabled = true };
        var provider = new SessionLogBufferProvider(buffer);
        var logger = provider.CreateLogger("TestCategory");

        logger.LogWarning("Warning message");

        var entries = buffer.Drain();
        Assert.Single(entries);
        Assert.Equal("Warn", entries[0].Level);
    }

    [Fact]
    public void Logger_WhenBufferEnabled_LogsErrorLevel()
    {
        var buffer = new SessionLogBuffer { Enabled = true };
        var provider = new SessionLogBufferProvider(buffer);
        var logger = provider.CreateLogger("TestCategory");

        logger.LogError("Error message");

        var entries = buffer.Drain();
        Assert.Single(entries);
        Assert.Equal("Error", entries[0].Level);
    }

    [Fact]
    public void Logger_WhenBufferEnabled_LogsCriticalAsError()
    {
        var buffer = new SessionLogBuffer { Enabled = true };
        var provider = new SessionLogBufferProvider(buffer);
        var logger = provider.CreateLogger("TestCategory");

        logger.LogCritical("Critical message");

        var entries = buffer.Drain();
        Assert.Single(entries);
        Assert.Equal("Error", entries[0].Level);
    }

    [Fact]
    public void Logger_WhenBufferEnabled_SkipsDebugLevel()
    {
        var buffer = new SessionLogBuffer { Enabled = true };
        var provider = new SessionLogBufferProvider(buffer);
        var logger = provider.CreateLogger("TestCategory");

        logger.LogDebug("Debug message");

        var entries = buffer.Drain();
        Assert.Empty(entries);
    }

    [Fact]
    public void Logger_WhenBufferEnabled_SkipsTraceLevel()
    {
        var buffer = new SessionLogBuffer { Enabled = true };
        var provider = new SessionLogBufferProvider(buffer);
        var logger = provider.CreateLogger("TestCategory");

        logger.LogTrace("Trace message");

        var entries = buffer.Drain();
        Assert.Empty(entries);
    }

    [Fact]
    public void Logger_WhenBufferDisabled_SkipsAllLevels()
    {
        var buffer = new SessionLogBuffer { Enabled = false };
        var provider = new SessionLogBufferProvider(buffer);
        var logger = provider.CreateLogger("TestCategory");

        logger.LogInformation("Info");
        logger.LogWarning("Warn");
        logger.LogError("Error");

        var entries = buffer.Drain();
        Assert.Empty(entries);
    }

    [Fact]
    public void Logger_IsEnabled_ReflectsBufferState()
    {
        var buffer = new SessionLogBuffer { Enabled = true };
        var provider = new SessionLogBufferProvider(buffer);
        var logger = provider.CreateLogger("TestCategory");

        Assert.True(logger.IsEnabled(LogLevel.Information));
        Assert.True(logger.IsEnabled(LogLevel.Warning));
        Assert.True(logger.IsEnabled(LogLevel.Error));
        Assert.True(logger.IsEnabled(LogLevel.Critical));
        Assert.False(logger.IsEnabled(LogLevel.Debug));
        Assert.False(logger.IsEnabled(LogLevel.Trace));
    }

    [Fact]
    public void Logger_IsEnabled_FalseWhenBufferDisabled()
    {
        var buffer = new SessionLogBuffer { Enabled = false };
        var provider = new SessionLogBufferProvider(buffer);
        var logger = provider.CreateLogger("TestCategory");

        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.False(logger.IsEnabled(LogLevel.Warning));
        Assert.False(logger.IsEnabled(LogLevel.Error));
    }

    [Fact]
    public void Logger_WithException_AppendsExceptionInfo()
    {
        var buffer = new SessionLogBuffer { Enabled = true };
        var provider = new SessionLogBufferProvider(buffer);
        var logger = provider.CreateLogger("TestCategory");

        var exception = new InvalidOperationException("Something went wrong");
        logger.LogError(exception, "Operation failed");

        var entries = buffer.Drain();
        Assert.Single(entries);
        Assert.Contains("Operation failed", entries[0].Message);
        Assert.Contains("InvalidOperationException", entries[0].Message);
        Assert.Contains("Something went wrong", entries[0].Message);
    }

    [Fact]
    public void Logger_BeginScope_ReturnsNull()
    {
        var buffer = new SessionLogBuffer { Enabled = true };
        var provider = new SessionLogBufferProvider(buffer);
        var logger = provider.CreateLogger("TestCategory");

        var scope = logger.BeginScope("test scope");

        Assert.Null(scope);
    }

    [Fact]
    public void Provider_Dispose_DoesNotThrow()
    {
        var buffer = new SessionLogBuffer { Enabled = true };
        var provider = new SessionLogBufferProvider(buffer);

        provider.Dispose(); // Should not throw
    }

    [Fact]
    public void Logger_DifferentCategories_PreservedInEntries()
    {
        var buffer = new SessionLogBuffer { Enabled = true };
        var provider = new SessionLogBufferProvider(buffer);

        var logger1 = provider.CreateLogger("Category.A");
        var logger2 = provider.CreateLogger("Category.B");

        logger1.LogInformation("From A");
        logger2.LogInformation("From B");

        var entries = buffer.Drain();
        Assert.Equal(2, entries.Count);
        Assert.Equal("Category.A", entries[0].Category);
        Assert.Equal("Category.B", entries[1].Category);
    }
}
