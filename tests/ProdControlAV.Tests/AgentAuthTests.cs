using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using ProdControlAV.API.Services;
using ProdControlAV.Infrastructure.Services;
using Xunit;

namespace ProdControlAV.Tests;

public class AgentAuthTests
{
    [Fact]
    public async Task ValidateAsync_LogsComputedHash_AtDebugLevel()
    {
        // Arrange
        var mockAuthStore = new Mock<IAgentAuthStore>();
        var mockLogger = new Mock<ILogger<AgentAuth>>();
        
        mockAuthStore
            .Setup(x => x.ValidateAgentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentAuthDto?)null);
        
        var agentAuth = new AgentAuth(mockAuthStore.Object, mockLogger.Object);
        var testAgentKey = "test-agent-key-12345";

        // Act
        await agentAuth.ValidateAsync(testAgentKey, CancellationToken.None);

        // Assert - Verify Debug log was called with the computed hash
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Computed agent key hash for incoming agent")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        
        // Verify that the raw agent key is NOT logged
        mockLogger.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(testAgentKey)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never,
            "Raw agent key should never be logged");
    }

    [Fact]
    public void HashAgentKey_ProducesUppercaseHexString()
    {
        // Arrange
        var mockAuthStore = new Mock<IAgentAuthStore>();
        var mockLogger = new Mock<ILogger<AgentAuth>>();
        var agentAuth = new AgentAuth(mockAuthStore.Object, mockLogger.Object);
        var testKey = "test-key";

        // Act
        var hash = agentAuth.HashAgentKey(testKey);

        // Assert
        Assert.NotEmpty(hash);
        Assert.Equal(64, hash.Length); // SHA256 produces 32 bytes = 64 hex chars
        Assert.Matches("^[A-F0-9]+$", hash); // Should be uppercase hex
    }

    [Fact]
    public async Task ValidateAsync_WithEmptyKey_ReturnsErrorWithoutLogging()
    {
        // Arrange
        var mockAuthStore = new Mock<IAgentAuthStore>();
        var mockLogger = new Mock<ILogger<AgentAuth>>();
        var agentAuth = new AgentAuth(mockAuthStore.Object, mockLogger.Object);

        // Act
        var (agent, error) = await agentAuth.ValidateAsync("", CancellationToken.None);

        // Assert
        Assert.Null(agent);
        Assert.Equal("missing_agent_key", error);
        
        // Verify no logging occurred for empty key
        mockLogger.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }
}
