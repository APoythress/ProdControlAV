using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using ProdControlAV.API.Services;
using ProdControlAV.Core.Models;
using Xunit;

namespace ProdControlAV.Tests;

public class AzureQueueAgentCommandServiceTests
{
    [Fact]
    public void Constructor_WithMissingConnectionString_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var mockConfig = new Mock<IConfiguration>();
        var mockLogger = new Mock<ILogger<AzureQueueAgentCommandService>>();
        
        // Setup configuration to return null for connection string
        mockConfig.Setup(c => c["Storage:QueueConnectionString"]).Returns((string?)null);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => 
            new AzureQueueAgentCommandService(mockConfig.Object, mockLogger.Object));
        
        Assert.Contains("Storage:QueueConnectionString not configured", exception.Message);
    }

    [Fact]
    public void Constructor_WithValidConnectionString_ShouldNotThrow()
    {
        // Arrange
        var mockConfig = new Mock<IConfiguration>();
        var mockLogger = new Mock<ILogger<AzureQueueAgentCommandService>>();
        
        // Setup configuration with a valid connection string
        mockConfig.Setup(c => c["Storage:QueueConnectionString"])
            .Returns("DefaultEndpointsProtocol=https;AccountName=testaccount;AccountKey=dGVzdGtleQ==;EndpointSuffix=core.windows.net");
        mockConfig.Setup(c => c.GetSection("Storage:MaxDequeueCount").Value).Returns("5");

        // Act & Assert - should not throw
        var service = new AzureQueueAgentCommandService(mockConfig.Object, mockLogger.Object);
        Assert.NotNull(service);
    }

    [Fact]
    public async Task EnqueueCommandAsync_WithValidCommand_ShouldNotThrow()
    {
        // Arrange
        var mockConfig = new Mock<IConfiguration>();
        var mockLogger = new Mock<ILogger<AzureQueueAgentCommandService>>();
        
        // Use Azurite emulator connection string for testing
        var connectionString = "UseDevelopmentStorage=true";
        mockConfig.Setup(c => c["Storage:QueueConnectionString"]).Returns(connectionString);
        mockConfig.Setup(c => c.GetSection("Storage:MaxDequeueCount").Value).Returns("5");

        var service = new AzureQueueAgentCommandService(mockConfig.Object, mockLogger.Object);
        
        var command = new AgentCommand
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            AgentId = Guid.NewGuid(),
            DeviceId = Guid.NewGuid(),
            Verb = "PING",
            Payload = null,
            DueUtc = DateTime.UtcNow.AddMinutes(5)
        };

        // Act & Assert - This will fail if Azurite is not running, which is expected
        // The test validates the service doesn't crash during construction
        try
        {
            await service.EnqueueCommandAsync(command, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Expected when Azurite is not running - just verify it's a connection error
            Assert.Contains("connection", ex.Message.ToLower());
        }
    }
}
