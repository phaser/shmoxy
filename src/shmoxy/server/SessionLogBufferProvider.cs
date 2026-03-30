using Microsoft.Extensions.Logging;

namespace shmoxy.server;

/// <summary>
/// Logging provider that writes to SessionLogBuffer, allowing all application
/// logs to be captured alongside inspection sessions.
/// </summary>
public class SessionLogBufferProvider : ILoggerProvider
{
    private readonly SessionLogBuffer _buffer;

    public SessionLogBufferProvider(SessionLogBuffer buffer)
    {
        _buffer = buffer;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new SessionLogBufferLogger(_buffer, categoryName);
    }

    public void Dispose() { }

    private class SessionLogBufferLogger : ILogger
    {
        private readonly SessionLogBuffer _buffer;
        private readonly string _category;

        public SessionLogBufferLogger(SessionLogBuffer buffer, string category)
        {
            _buffer = buffer;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => _buffer.Enabled && logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var level = logLevel switch
            {
                LogLevel.Warning => "Warn",
                LogLevel.Error or LogLevel.Critical => "Error",
                _ => "Info"
            };

            var message = formatter(state, exception);
            if (exception != null)
                message += $" | {exception.GetType().Name}: {exception.Message}";

            _buffer.Log(level, _category, message);
        }
    }
}
