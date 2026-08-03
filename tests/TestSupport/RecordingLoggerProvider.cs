using Microsoft.Extensions.Logging;

namespace Axinyp.Wpf.Hosting.TestSupport;

internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    public List<LogEntry> Entries { get; } = [];

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(Entries);

    public void Dispose() { }

    private sealed class RecordingLogger(List<LogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, object?>();
            entries.Add(new LogEntry(logLevel, eventId, exception, properties));
        }
    }
}

internal sealed record LogEntry(
    LogLevel LogLevel,
    EventId EventId,
    Exception? Exception,
    IReadOnlyDictionary<string, object?> Properties);
