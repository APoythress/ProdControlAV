using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ProdControlAV.Agent.Services;
using ProdControlAV.Core.Models;
using Xunit;

namespace ProdControlAV.Tests;

public class CommandServiceTests
{
    [Fact]
    public async Task ExecuteCommandAsync_WithPingCommand_CompletesSuccessfully()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<CommandService>>();
        var mockHttpClient = new Mock<HttpClient>();
        var mockJwtAuth = new Mock<IJwtAuthService>();
        var apiOptions = new ApiOptions
        {
            BaseUrl = "https://test.com/api",
            DevicesEndpoint = "/agents/devices",
            StatusEndpoint = "/agents/status",
            HeartbeatEndpoint = "/agents/heartbeat",
            CommandsEndpoint = "/agents/commands/next",
            CommandCompleteEndpoint = "/agents/commands/complete",
            ApiKey = "test-key"
        };

        var service = new CommandService(mockHttpClient.Object, mockLogger.Object, Options.Create(apiOptions), mockJwtAuth.Object);

        var command = new CommandEnvelope
        {
            CommandId = Guid.NewGuid(),
            DeviceId = Guid.NewGuid(),
            Verb = "PING",
            Payload = null
        };

        // Act & Assert - Should not throw
        await service.ExecuteCommandAsync(command, CancellationToken.None);

        // Verify logging occurred
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Executing ping command")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteCommandAsync_WithUnknownCommand_LogsWarning()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<CommandService>>();
        var mockHttpClient = new Mock<HttpClient>();
        var mockJwtAuth = new Mock<IJwtAuthService>();
        var apiOptions = new ApiOptions
        {
            BaseUrl = "https://test.com/api",
            DevicesEndpoint = "/agents/devices",
            StatusEndpoint = "/agents/status",
            HeartbeatEndpoint = "/agents/heartbeat",
            CommandsEndpoint = "/agents/commands/next",
            CommandCompleteEndpoint = "/agents/commands/complete",
            ApiKey = "test-key"
        };

        var service = new CommandService(mockHttpClient.Object, mockLogger.Object, Options.Create(apiOptions), mockJwtAuth.Object);

        var command = new CommandEnvelope
        {
            CommandId = Guid.NewGuid(),
            DeviceId = Guid.NewGuid(),
            Verb = "MALICIOUS_COMMAND",
            Payload = null
        };

        // Act & Assert - Should not throw
        await service.ExecuteCommandAsync(command, CancellationToken.None);

        // Verify warning was logged for unknown command
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Unknown or unauthorized command")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteCommandAsync_WithRESTCommand_HandlesDeviceErrors()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<CommandService>>();
        var mockHttpClient = new Mock<HttpClient>();
        var mockJwtAuth = new Mock<IJwtAuthService>();
        mockJwtAuth.Setup(x => x.GetValidTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-token");

        var apiOptions = new ApiOptions
        {
            BaseUrl = "https://test.com/api",
            DevicesEndpoint = "/agents/devices",
            StatusEndpoint = "/agents/status",
            HeartbeatEndpoint = "/agents/heartbeat",
            CommandsEndpoint = "/agents/commands/next",
            CommandCompleteEndpoint = "/agents/commands/complete",
            ApiKey = "test-key"
        };

        var service = new CommandService(mockHttpClient.Object, mockLogger.Object, Options.Create(apiOptions), mockJwtAuth.Object);

        // Create a REST command payload pointing to a non-existent device
        // Note: This is an integration-style test that uses the real HttpClient behavior
        // 192.0.2.1 is from TEST-NET-1 (RFC 5737) - a reserved IP that should not respond
        // This allows us to test real error handling for unreachable devices
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            commandType = "REST",
            deviceIp = "192.0.2.1", // Reserved IP that should not respond (TEST-NET-1)
            devicePort = 9999,
            commandData = "/test",
            httpMethod = "GET"
        });

        var command = new CommandEnvelope
        {
            CommandId = Guid.NewGuid(),
            DeviceId = Guid.NewGuid(),
            Verb = "REST",
            Payload = payload
        };

        // Act - Should not throw despite device being unreachable
        await service.ExecuteCommandAsync(command, CancellationToken.None);

        // Assert - Verify error logging occurred with device context
        mockLogger.Verify(
            x => x.Log(
                It.Is<LogLevel>(l => l == LogLevel.Warning || l == LogLevel.Error),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("192.0.2.1") && v.ToString().Contains("9999")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.AtLeastOnce);
    }
}