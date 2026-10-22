using Microsoft.Extensions.Logging;

namespace Dml.Launcher.Infrastructure.Logging;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly object gate = new();
    private readonly string logDirectory;
    private readonly InMemoryLogStore logStore;

    public FileLoggerProvider(AppPaths paths, InMemoryLogStore logStore)
    {
        logDirectory = paths.LogDirectory;
        this.logStore = logStore;
        Directory.CreateDirectory(logDirectory);
        RemoveExpiredLogs();
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, Write);

    public void Dispose()
    {
    }

    private void Write(string line)
    {
        string redacted = LogRedactor.Redact(line);
        lock (gate)
        {
            File.AppendAllText(Path.Combine(logDirectory, $"dml-{DateTimeOffset.Now:yyyyMMdd}.log"), redacted + Environment.NewLine);
            logStore.Add(redacted);
        }
    }

    private void RemoveExpiredLogs()
    {
        DateTime cutoff = DateTime.UtcNow.AddDays(-14);
        foreach (string path in Directory.EnumerateFiles(logDirectory, "dml-*.log"))
        {
            if (File.GetLastWriteTimeUtc(path) < cutoff)
            {
                File.Delete(path);
            }
        }
    }

    private sealed class FileLogger(string categoryName, Action<string> write) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            string line = $"{DateTimeOffset.Now:O} [{logLevel}] {categoryName}: {formatter(state, exception)}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            write(line);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
