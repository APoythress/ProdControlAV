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

    [Fact]
    public void CommandPayload_MatchesHyperDeckConfiguration()
    {
        // This test validates that the command payload structure matches
        // the user's HyperDeck "Start Recording" command configuration
        
        // Arrange - Simulate exact payload from user's command
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            commandName = "Start Recording",
            commandType = "REST",
            commandData = "/transports/0/record",
            httpMethod = "POST",
            requestBody = "{ \"clipName\": null }",
            requestHeaders = "{\"Authorization\": \"Bearer token\"}",
            deviceIp = "10.10.30.235",
            devicePort = 9993,
            deviceType = "Video",
            monitorRecordingStatus = true,
            statusEndpoint = "/api/recording/status",
            statusPollingIntervalSeconds = 60
        });

        // Act - Parse payload like the agent does
        var payloadJson = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(payload);
        
        var commandData = payloadJson.GetProperty("commandData").GetString();
        var httpMethod = payloadJson.GetProperty("httpMethod").GetString();
        var deviceIp = payloadJson.GetProperty("deviceIp").GetString();
        var devicePort = payloadJson.GetProperty("devicePort").GetInt32();
        var requestBody = payloadJson.GetProperty("requestBody").GetString();
        var requestHeaders = payloadJson.GetProperty("requestHeaders").GetString();
        
        // Build URL like the agent does
        var baseUri = new Uri($"http://{deviceIp}:{devicePort}");
        var path = commandData?.TrimStart('/') ?? "";
        var fullUri = new Uri(baseUri, path);

        // Assert - Verify all fields are correctly extracted
        Assert.Equal("/transports/0/record", commandData);
        Assert.Equal("POST", httpMethod);
        Assert.Equal("10.10.30.235", deviceIp);
        Assert.Equal(9993, devicePort);
        Assert.Equal("{ \"clipName\": null }", requestBody);
        Assert.Contains("Authorization", requestHeaders);
        Assert.Contains("Bearer token", requestHeaders);
        
        // Verify URL construction matches expected HyperDeck API endpoint
        Assert.Equal("http://10.10.30.235:9993/transports/0/record", fullUri.ToString());
        
        // Verify headers can be parsed
        var headers = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(requestHeaders);
        Assert.NotNull(headers);
        Assert.True(headers.ContainsKey("Authorization"));
        Assert.Equal("Bearer token", headers["Authorization"]);
    }
    
    [Fact]
    public async Task ExecuteCommandAsync_WithAtemCutCommand_CompletesSuccessfully()
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
            ApiKey = "test-key-with-at-least-32-characters-for-security"
        };

        var service = new CommandService(mockHttpClient.Object, mockLogger.Object, Options.Create(apiOptions), mockJwtAuth.Object);

        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            commandType = "ATEM",
            atemCommand = "CUT_TO_PROGRAM",
            inputId = 1
        });

        var command = new CommandEnvelope
        {
            CommandId = Guid.NewGuid(),
            DeviceId = Guid.NewGuid(),
            Verb = "ATEM_COMMAND",
            Payload = payload
        };

        // Act
        await service.ExecuteCommandAsync(command, CancellationToken.None);

        // Assert - Should log the ATEM command execution
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Executing ATEM command")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
    
    [Fact]
    public async Task ExecuteCommandAsync_WithAtemFadeCommand_CompletesSuccessfully()
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
            ApiKey = "test-key-with-at-least-32-characters-for-security"
        };

        var service = new CommandService(mockHttpClient.Object, mockLogger.Object, Options.Create(apiOptions), mockJwtAuth.Object);

        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            commandType = "ATEM",
            atemCommand = "FADE_TO_PROGRAM",
            inputId = 2,
            transitionRate = 45
        });

        var command = new CommandEnvelope
        {
            CommandId = Guid.NewGuid(),
            DeviceId = Guid.NewGuid(),
            Verb = "ATEM_COMMAND",
            Payload = payload
        };

        // Act
        await service.ExecuteCommandAsync(command, CancellationToken.None);

        // Assert
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("ATEM command")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
    
    [Fact]
    public async Task ExecuteCommandAsync_WithAtemSetPreviewCommand_CompletesSuccessfully()
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
            ApiKey = "test-key-with-at-least-32-characters-for-security"
        };

        var service = new CommandService(mockHttpClient.Object, mockLogger.Object, Options.Create(apiOptions), mockJwtAuth.Object);

        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            commandType = "ATEM",
            atemCommand = "SET_PREVIEW",
            inputId = 3
        });

        var command = new CommandEnvelope
        {
            CommandId = Guid.NewGuid(),
            DeviceId = Guid.NewGuid(),
            Verb = "ATEM_COMMAND",
            Payload = payload
        };

        // Act
        await service.ExecuteCommandAsync(command, CancellationToken.None);

        // Assert
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("ATEM command")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
    
    [Fact]
    public async Task ExecuteCommandAsync_WithAtemListMacrosCommand_CompletesSuccessfully()
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
            ApiKey = "test-key-with-at-least-32-characters-for-security"
        };

        var service = new CommandService(mockHttpClient.Object, mockLogger.Object, Options.Create(apiOptions), mockJwtAuth.Object);

        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            commandType = "ATEM",
            atemCommand = "LIST_MACROS"
        });

        var command = new CommandEnvelope
        {
            CommandId = Guid.NewGuid(),
            DeviceId = Guid.NewGuid(),
            Verb = "ATEM_COMMAND",
            Payload = payload
        };

        // Act
        await service.ExecuteCommandAsync(command, CancellationToken.None);

        // Assert
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("ATEM command")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
    
    [Fact]
    public async Task ExecuteCommandAsync_WithAtemRunMacroCommand_CompletesSuccessfully()
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
            ApiKey = "test-key-with-at-least-32-characters-for-security"
        };

        var service = new CommandService(mockHttpClient.Object, mockLogger.Object, Options.Create(apiOptions), mockJwtAuth.Object);

        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            commandType = "ATEM",
            atemCommand = "RUN_MACRO",
            macroId = 5
        });

        var command = new CommandEnvelope
        {
            CommandId = Guid.NewGuid(),
            DeviceId = Guid.NewGuid(),
            Verb = "ATEM_COMMAND",
            Payload = payload
        };

        // Act
        await service.ExecuteCommandAsync(command, CancellationToken.None);

        // Assert
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("ATEM command")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}