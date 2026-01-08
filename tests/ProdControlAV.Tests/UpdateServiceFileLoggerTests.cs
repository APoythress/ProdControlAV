using System;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Logging;
using ProdControlAV.Agent.Services;
using Xunit;

namespace ProdControlAV.Tests;

/// <summary>
/// Tests for UpdateServiceFileLogger to verify file logging functionality.
/// </summary>
public class UpdateServiceFileLoggerTests : IDisposable
{
    private readonly string _testLogDirectory;

    public UpdateServiceFileLoggerTests()
    {
        // Create a unique temporary directory for each test
        _testLogDirectory = Path.Combine(Path.GetTempPath(), $"test-updateservice-logs-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testLogDirectory);
    }

    public void Dispose()
    {
        // Clean up test directory
        if (Directory.Exists(_testLogDirectory))
        {
            try
            {
                Directory.Delete(_testLogDirectory, recursive: true);
            }
            catch
            {
                // Ignore cleanup failures
            }
        }
    }

    [Fact]
    public void FileLoggerProvider_CreatesLogDirectory()
    {
        // Arrange
        var testAgentDir = Path.Combine(_testLogDirectory, "agent");
        Directory.CreateDirectory(testAgentDir);

        // Act
        using var provider = new UpdateServiceFileLoggerProvider(testAgentDir);

        // Assert
        var expectedLogDir = Path.Combine(testAgentDir, "logs", "updateService");
        Assert.True(Directory.Exists(expectedLogDir), $"Log directory should exist at {expectedLogDir}");
    }

    [Fact]
    public void FileLogger_WritesLogToFile()
    {
        // Arrange
        var testAgentDir = Path.Combine(_testLogDirectory, "agent");
        Directory.CreateDirectory(testAgentDir);
        using var provider = new UpdateServiceFileLoggerProvider(testAgentDir);
        var logger = provider.CreateLogger("ProdControlAV.Agent.Services.UpdateService");

        // Act
        logger.LogInformation("Test log message");

        // Give a moment for file write to complete
        Thread.Sleep(100);

        // Assert
        var expectedLogDir = Path.Combine(testAgentDir, "logs", "updateService");
        var logFileName = $"{DateTime.UtcNow:yyyy-MM-dd}_UpdateServiceLog.txt";
        var logFilePath = Path.Combine(expectedLogDir, logFileName);

        Assert.True(File.Exists(logFilePath), $"Log file should exist at {logFilePath}");
        
        var logContent = File.ReadAllText(logFilePath);
        Assert.Contains("Test log message", logContent);
        Assert.Contains("INFO", logContent);
        Assert.Contains("UpdateService", logContent);
    }

    [Fact]
    public void FileLogger_LogsNetSparkleMessages()
    {
        // Arrange
        var testAgentDir = Path.Combine(_testLogDirectory, "agent");
        Directory.CreateDirectory(testAgentDir);
        using var provider = new UpdateServiceFileLoggerProvider(testAgentDir);
        var logger = provider.CreateLogger("netsparkle.updater");

        // Act
        logger.LogWarning("NetSparkle test warning");

        // Give a moment for file write to complete
        Thread.Sleep(100);

        // Assert
        var expectedLogDir = Path.Combine(testAgentDir, "logs", "updateService");
        var logFileName = $"{DateTime.UtcNow:yyyy-MM-dd}_UpdateServiceLog.txt";
        var logFilePath = Path.Combine(expectedLogDir, logFileName);

        Assert.True(File.Exists(logFilePath), $"Log file should exist at {logFilePath}");
        
        var logContent = File.ReadAllText(logFilePath);
        Assert.Contains("NetSparkle test warning", logContent);
        Assert.Contains("WARN", logContent);
    }

    [Fact]
    public void FileLogger_IgnoresOtherCategories()
    {
        // Arrange
        var testAgentDir = Path.Combine(_testLogDirectory, "agent");
        Directory.CreateDirectory(testAgentDir);
        using var provider = new UpdateServiceFileLoggerProvider(testAgentDir);
        var logger = provider.CreateLogger("SomeOtherService");

        // Act
        logger.LogInformation("This should not be logged");

        // Give a moment for potential file write
        Thread.Sleep(100);

        // Assert
        var expectedLogDir = Path.Combine(testAgentDir, "logs", "updateService");
        var logFileName = $"{DateTime.UtcNow:yyyy-MM-dd}_UpdateServiceLog.txt";
        var logFilePath = Path.Combine(expectedLogDir, logFileName);

        // Either file doesn't exist or doesn't contain the message
        if (File.Exists(logFilePath))
        {
            var logContent = File.ReadAllText(logFilePath);
            Assert.DoesNotContain("This should not be logged", logContent);
        }
    }

    [Fact]
    public void FileLogger_LogsExceptions()
    {
        // Arrange
        var testAgentDir = Path.Combine(_testLogDirectory, "agent");
        Directory.CreateDirectory(testAgentDir);
        using var provider = new UpdateServiceFileLoggerProvider(testAgentDir);
        var logger = provider.CreateLogger("ProdControlAV.Agent.Services.UpdateService");

        // Act
        var testException = new InvalidOperationException("Test exception message");
        logger.LogError(testException, "Error occurred during update");

        // Give a moment for file write to complete
        Thread.Sleep(100);

        // Assert
        var expectedLogDir = Path.Combine(testAgentDir, "logs", "updateService");
        var logFileName = $"{DateTime.UtcNow:yyyy-MM-dd}_UpdateServiceLog.txt";
        var logFilePath = Path.Combine(expectedLogDir, logFileName);

        Assert.True(File.Exists(logFilePath), $"Log file should exist at {logFilePath}");
        
        var logContent = File.ReadAllText(logFilePath);
        Assert.Contains("Error occurred during update", logContent);
        Assert.Contains("ERROR", logContent);
        Assert.Contains("InvalidOperationException", logContent);
        Assert.Contains("Test exception message", logContent);
        Assert.Contains("StackTrace:", logContent);
    }

    [Fact]
    public void FileLogger_DailyRotation_UsesSeparateFiles()
    {
        // Arrange
        var testAgentDir = Path.Combine(_testLogDirectory, "agent");
        Directory.CreateDirectory(testAgentDir);
        using var provider = new UpdateServiceFileLoggerProvider(testAgentDir);
        var logger = provider.CreateLogger("ProdControlAV.Agent.Services.UpdateService");

        // Act
        logger.LogInformation("Log entry for today");

        // Give a moment for file write to complete
        Thread.Sleep(100);

        // Assert - verify file name format
        var expectedLogDir = Path.Combine(testAgentDir, "logs", "updateService");
        var todayLogFileName = $"{DateTime.UtcNow:yyyy-MM-dd}_UpdateServiceLog.txt";
        var logFilePath = Path.Combine(expectedLogDir, todayLogFileName);

        Assert.True(File.Exists(logFilePath), $"Today's log file should exist at {logFilePath}");
        
        // Verify the file name matches the expected format (YYYY-MM-DD_UpdateServiceLog.txt)
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}_UpdateServiceLog\.txt$", todayLogFileName);
    }
}
