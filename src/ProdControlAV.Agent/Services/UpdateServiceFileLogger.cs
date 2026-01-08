using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ProdControlAV.Agent.Services;

/// <summary>
/// File logger provider that creates daily rotating log files specifically for UpdateService logging.
/// Logs are written to logs/updateService/ folder relative to the agent installation directory.
/// </summary>
public sealed class UpdateServiceFileLoggerProvider : ILoggerProvider
{
    private readonly string _logDirectory;
    private readonly ConcurrentDictionary<string, UpdateServiceFileLogger> _loggers = new();
    private readonly string _categoryFilter;

    public UpdateServiceFileLoggerProvider(string agentDirectory, string categoryFilter = "ProdControlAV.Agent.Services.UpdateService")
    {
        _categoryFilter = categoryFilter;
        _logDirectory = Path.Combine(agentDirectory, "logs", "updateService");
        
        // Ensure log directory exists
        Directory.CreateDirectory(_logDirectory);
    }

    public ILogger CreateLogger(string categoryName)
    {
        // Only create loggers for UpdateService and NetSparkle categories
        if (!categoryName.StartsWith(_categoryFilter) && !categoryName.Contains("netsparkle", StringComparison.OrdinalIgnoreCase))
        {
            return new NullLogger();
        }

        return _loggers.GetOrAdd(categoryName, name => new UpdateServiceFileLogger(name, _logDirectory));
    }

    public void Dispose()
    {
        _loggers.Clear();
    }

    /// <summary>
    /// Null logger that does nothing - used for categories we don't want to log
    /// </summary>
    private sealed class NullLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}

/// <summary>
/// File logger that writes log messages to daily rotating files in YYYY-MM-DD_UpdateServiceLog.txt format.
/// Thread-safe implementation with file locking.
/// </summary>
public sealed class UpdateServiceFileLogger : ILogger
{
    private readonly string _categoryName;
    private readonly string _logDirectory;
    private static readonly object _fileLock = new object();

    public UpdateServiceFileLogger(string categoryName, string logDirectory)
    {
        _categoryName = categoryName;
        _logDirectory = logDirectory;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        try
        {
            var message = formatter(state, exception);
            var logFileName = $"{DateTime.UtcNow:yyyy-MM-dd}_UpdateServiceLog.txt";
            var logFilePath = Path.Combine(_logDirectory, logFileName);

            var logEntry = FormatLogEntry(logLevel, _categoryName, message, exception);

            lock (_fileLock)
            {
                File.AppendAllText(logFilePath, logEntry + Environment.NewLine);
            }
        }
        catch
        {
            // Silently fail if we can't write to log file to avoid breaking the application
        }
    }

    private static string FormatLogEntry(LogLevel logLevel, string categoryName, string message, Exception? exception)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
        var levelString = logLevel switch
        {
            LogLevel.Trace => "TRACE",
            LogLevel.Debug => "DEBUG",
            LogLevel.Information => "INFO ",
            LogLevel.Warning => "WARN ",
            LogLevel.Error => "ERROR",
            LogLevel.Critical => "CRIT ",
            _ => "INFO "
        };

        var categoryShort = categoryName.Contains("UpdateService") ? "UpdateService" : 
                           categoryName.Contains("netsparkle", StringComparison.OrdinalIgnoreCase) ? "NetSparkle" : 
                           categoryName;

        var logEntry = $"[{timestamp}] [{levelString}] [{categoryShort}] {message}";

        if (exception != null)
        {
            logEntry += $"{Environment.NewLine}Exception: {exception.GetType().FullName}: {exception.Message}";
            logEntry += $"{Environment.NewLine}StackTrace: {exception.StackTrace}";
            
            if (exception.InnerException != null)
            {
                logEntry += $"{Environment.NewLine}InnerException: {exception.InnerException.GetType().FullName}: {exception.InnerException.Message}";
            }
        }

        return logEntry;
    }
}
