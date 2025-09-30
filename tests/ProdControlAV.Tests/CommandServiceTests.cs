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
}