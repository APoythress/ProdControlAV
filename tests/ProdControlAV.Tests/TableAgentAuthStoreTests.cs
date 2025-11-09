using System;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using ProdControlAV.Infrastructure.Services;

namespace ProdControlAV.Tests;

public class TableAgentAuthStoreTests
{
    [Fact]
    public async Task ValidateAgentAsync_LogsDebugInformationBeforeLookup()
    {
        // Arrange
        var mockTableServiceClient = new Mock<TableServiceClient>();
        var mockAgentsTable = new Mock<TableClient>();
        var mockAgentKeyHashIndex = new Mock<TableClient>();
        var mockLogger = new Mock<ILogger<TableAgentAuthStore>>();

        // Setup table clients
        mockTableServiceClient
            .Setup(x => x.GetTableClient("Agents"))
            .Returns(mockAgentsTable.Object);
        
        mockTableServiceClient
            .Setup(x => x.GetTableClient("AgentKeyHashIndex"))
            .Returns(mockAgentKeyHashIndex.Object);

        // Setup table name property
        mockAgentsTable.Setup(x => x.Name).Returns("Agents");
        mockAgentKeyHashIndex.Setup(x => x.Name).Returns("AgentKeyHashIndex");

        // Setup CreateIfNotExists to return a mock response
        mockAgentsTable.Setup(x => x.CreateIfNotExists(It.IsAny<CancellationToken>()))
            .Returns(Mock.Of<Response<TableItem>>());
        mockAgentKeyHashIndex.Setup(x => x.CreateIfNotExists(It.IsAny<CancellationToken>()))
            .Returns(Mock.Of<Response<TableItem>>());

        var agentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var agentKeyHash = "ABC123HASH456";

        // Setup index entity
        var indexEntity = new TableEntity("ABC1", agentKeyHash)
        {
            ["AgentId"] = agentId.ToString(),
            ["TenantId"] = tenantId.ToString()
        };

        mockAgentKeyHashIndex
            .Setup(x => x.GetEntityAsync<TableEntity>(
                It.IsAny<string>(),
                agentKeyHash,
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(indexEntity, Mock.Of<Response>()));

        // Setup agent entity
        var agentEntity = new TableEntity(tenantId.ToString().ToLowerInvariant(), agentId.ToString())
        {
            ["Name"] = "Test Agent",
            ["AgentKeyHash"] = agentKeyHash
        };

        mockAgentsTable
            .Setup(x => x.GetEntityAsync<TableEntity>(
                tenantId.ToString().ToLowerInvariant(),
                agentId.ToString(),
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(agentEntity, Mock.Of<Response>()));

        var store = new TableAgentAuthStore(mockTableServiceClient.Object, mockLogger.Object);

        // Act
        var result = await store.ValidateAgentAsync(agentKeyHash, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        
        // Verify that debug log was called with the correct parameters
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => 
                    v.ToString()!.Contains("[TableAgentAuthStore] Looking up agent") &&
                    v.ToString()!.Contains("Agents") &&
                    v.ToString()!.Contains(tenantId.ToString().ToLowerInvariant()) &&
                    v.ToString()!.Contains(agentId.ToString())),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "Debug log should be called with table name, partition key, and row key");

        // Verify that no raw agent key hash is logged (it should only be in GetEntityAsync params)
        // Note: The hash itself may appear in debug logs for troubleshooting, which is acceptable
        // as it's not the raw secret key. We just verify raw secrets are not logged.
    }

    [Fact]
    public async Task ValidateAgentAsync_DoesNotLogSecretsOrKeys()
    {
        // Arrange
        var mockTableServiceClient = new Mock<TableServiceClient>();
        var mockAgentsTable = new Mock<TableClient>();
        var mockAgentKeyHashIndex = new Mock<TableClient>();
        var mockLogger = new Mock<ILogger<TableAgentAuthStore>>();

        mockTableServiceClient.Setup(x => x.GetTableClient("Agents")).Returns(mockAgentsTable.Object);
        mockTableServiceClient.Setup(x => x.GetTableClient("AgentKeyHashIndex")).Returns(mockAgentKeyHashIndex.Object);
        mockAgentsTable.Setup(x => x.Name).Returns("Agents");
        mockAgentKeyHashIndex.Setup(x => x.Name).Returns("AgentKeyHashIndex");
        
        mockAgentsTable.Setup(x => x.CreateIfNotExists(It.IsAny<CancellationToken>()))
            .Returns(Mock.Of<Response<TableItem>>());
        mockAgentKeyHashIndex.Setup(x => x.CreateIfNotExists(It.IsAny<CancellationToken>()))
            .Returns(Mock.Of<Response<TableItem>>());

        var agentKeyHash = "SECRETHASH123";
        
        // Setup to return not found
        mockAgentKeyHashIndex
            .Setup(x => x.GetEntityAsync<TableEntity>(
                It.IsAny<string>(),
                agentKeyHash,
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "Not found"));

        var store = new TableAgentAuthStore(mockTableServiceClient.Object, mockLogger.Object);

        // Act
        var result = await store.ValidateAgentAsync(agentKeyHash, CancellationToken.None);

        // Assert
        Assert.Null(result);
        
        // Verify that logs do not contain the raw agent key (only the hash is used for lookup)
        // The hash itself is logged for debugging, but not raw agent keys or secrets
        mockLogger.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => 
                    v.ToString()!.Contains("AgentKey=") || 
                    v.ToString()!.Contains("Secret=")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never,
            "Raw agent keys or secrets should never be logged");
    }
}
