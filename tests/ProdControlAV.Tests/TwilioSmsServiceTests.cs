using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ProdControlAV.Infrastructure.Services;
using Xunit;

namespace ProdControlAV.Tests;

public class TwilioSmsServiceTests
{
    [Fact]
    public async Task SendSmsAsync_WhenNotConfigured_ReturnsFalse()
    {
        // Arrange
        var logger = new Mock<ILogger<TwilioSmsService>>();
        var config = new TwilioConfig(); // Empty config
        var options = Options.Create(config);
        
        var smsService = new TwilioSmsService(options, logger.Object);

        // Act
        var result = await smsService.SendSmsAsync("+15551234567", "Test message", CancellationToken.None);

        // Assert
        Assert.False(result);
        logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Twilio SMS service not configured")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SendSmsAsync_WithEmptyPhoneNumber_ReturnsFalse()
    {
        // Arrange
        var logger = new Mock<ILogger<TwilioSmsService>>();
        var config = new TwilioConfig
        {
            AccountSid = "ACtest123",
            AuthToken = "test_token",
            FromPhoneNumber = "+15559999999"
        };
        var options = Options.Create(config);
        
        var smsService = new TwilioSmsService(options, logger.Object);

        // Act
        var result = await smsService.SendSmsAsync("", "Test message", CancellationToken.None);

        // Assert
        Assert.False(result);
        logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Phone number is empty")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SendSmsAsync_WithEmptyMessage_ReturnsFalse()
    {
        // Arrange
        var logger = new Mock<ILogger<TwilioSmsService>>();
        var config = new TwilioConfig
        {
            AccountSid = "ACtest123",
            AuthToken = "test_token",
            FromPhoneNumber = "+15559999999"
        };
        var options = Options.Create(config);
        
        var smsService = new TwilioSmsService(options, logger.Object);

        // Act
        var result = await smsService.SendSmsAsync("+15551234567", "", CancellationToken.None);

        // Assert
        Assert.False(result);
        logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Message is empty")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void Constructor_WithValidConfig_LogsInitialized()
    {
        // Arrange
        var logger = new Mock<ILogger<TwilioSmsService>>();
        var config = new TwilioConfig
        {
            AccountSid = "ACtest123",
            AuthToken = "test_token",
            FromPhoneNumber = "+15559999999"
        };
        var options = Options.Create(config);

        // Act
        _ = new TwilioSmsService(options, logger.Object);

        // Assert
        logger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Twilio SMS service initialized successfully")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
