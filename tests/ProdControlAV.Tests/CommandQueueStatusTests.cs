using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Moq;
using ProdControlAV.Infrastructure.Services;
using Xunit;

namespace ProdControlAV.Tests;

/// <summary>
/// Tests for command queue status updates (Succeeded/Failed)
/// Validates that commands are properly marked before dequeue
/// </summary>
public class CommandQueueStatusTests
{
    private readonly Mock<ILogger<TableCommandQueueStore>> _mockLogger;

    public CommandQueueStatusTests()
    {
        _mockLogger = new Mock<ILogger<TableCommandQueueStore>>();
    }

    [Fact]
    public async Task MarkAsSucceededAsync_ShouldUpdateStatusToSucceeded()
    {
        // Arrange
        var mockTableClient = new Mock<TableClient>();
        var tenantId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        var partitionKey = tenantId.ToString().ToLowerInvariant();
        var rowKey = $"{commandId:N}_20260131120000000";

        var existingEntity = new TableEntity(partitionKey, rowKey)
        {
            ["CommandId"] = commandId.ToString(),
            ["Status"] = "Processing",
            ["DeviceId"] = Guid.NewGuid().ToString(),
            ["CommandName"] = "Test Command",
            ["CommandType"] = "REST",
            ["QueuedUtc"] = DateTimeOffset.UtcNow,
            ["QueuedByUserId"] = Guid.NewGuid().ToString(),
            ["AttemptCount"] = 1
        };

        var asyncPageable = AsyncPageable<TableEntity>.FromPages(new[] 
        { 
            Page<TableEntity>.FromValues(new[] { existingEntity }, null, Mock.Of<Response>()) 
        });

        mockTableClient
            .Setup(x => x.QueryAsync<TableEntity>(
                It.IsAny<string>(), 
                It.IsAny<int?>(), 
                It.IsAny<IEnumerable<string>>(), 
                It.IsAny<CancellationToken>()))
            .Returns(asyncPageable);

        mockTableClient
            .Setup(x => x.UpdateEntityAsync(
                It.Is<TableEntity>(e => 
                    e["Status"].ToString() == "Succeeded" && 
                    e.ContainsKey("CompletedUtc")),
                It.IsAny<ETag>(),
                It.IsAny<TableUpdateMode>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(Mock.Of<Response>()));

        var mockSvc = new Mock<TableServiceClient>();
        mockSvc.Setup(s => s.GetTableClient(It.IsAny<string>())).Returns(mockTableClient.Object);
        var store = new TableCommandQueueStore(mockSvc.Object, _mockLogger.Object);

        // Act
        await store.MarkAsSucceededAsync(tenantId, commandId, CancellationToken.None);

        // Assert
        mockTableClient.Verify(x => x.UpdateEntityAsync(
            It.Is<TableEntity>(e => 
                e["Status"].ToString() == "Succeeded" && 
                e.ContainsKey("CompletedUtc")),
            It.IsAny<ETag>(),
            TableUpdateMode.Replace,
            It.IsAny<CancellationToken>()), 
            Times.Once);

        // Verify logging
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Marked command") && v.ToString().Contains("Succeeded")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task MarkAsFailedAsync_ShouldUpdateStatusToFailed()
    {
        // Arrange
        var mockTableClient = new Mock<TableClient>();
        var tenantId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        var partitionKey = tenantId.ToString().ToLowerInvariant();
        var rowKey = $"{commandId:N}_20260131120000000";

        var existingEntity = new TableEntity(partitionKey, rowKey)
        {
            ["CommandId"] = commandId.ToString(),
            ["Status"] = "Processing",
            ["DeviceId"] = Guid.NewGuid().ToString(),
            ["CommandName"] = "Test Command",
            ["CommandType"] = "REST",
            ["QueuedUtc"] = DateTimeOffset.UtcNow,
            ["QueuedByUserId"] = Guid.NewGuid().ToString(),
            ["AttemptCount"] = 3
        };

        var asyncPageable = AsyncPageable<TableEntity>.FromPages(new[] 
        { 
            Page<TableEntity>.FromValues(new[] { existingEntity }, null, Mock.Of<Response>()) 
        });

        mockTableClient
            .Setup(x => x.QueryAsync<TableEntity>(
                It.IsAny<string>(), 
                It.IsAny<int?>(), 
                It.IsAny<IEnumerable<string>>(), 
                It.IsAny<CancellationToken>()))
            .Returns(asyncPageable);

        mockTableClient
            .Setup(x => x.UpdateEntityAsync(
                It.Is<TableEntity>(e => 
                    e["Status"].ToString() == "Failed" && 
                    e.ContainsKey("FailedUtc")),
                It.IsAny<ETag>(),
                It.IsAny<TableUpdateMode>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(Mock.Of<Response>()));

        var mockSvc2 = new Mock<TableServiceClient>();
        mockSvc2.Setup(s => s.GetTableClient(It.IsAny<string>())).Returns(mockTableClient.Object);
        var store = new TableCommandQueueStore(mockSvc2.Object, _mockLogger.Object);

        // Act
        await store.MarkAsFailedAsync(tenantId, commandId, CancellationToken.None);

        // Assert
        mockTableClient.Verify(x => x.UpdateEntityAsync(
            It.Is<TableEntity>(e => 
                e["Status"].ToString() == "Failed" && 
                e.ContainsKey("FailedUtc")),
            It.IsAny<ETag>(),
            TableUpdateMode.Replace,
            It.IsAny<CancellationToken>()), 
            Times.Once);

        // Verify logging
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Marked command") && v.ToString().Contains("Failed")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task MarkAsSucceededAsync_WithNonExistentCommand_ShouldHandleGracefully()
    {
        // Arrange
        var mockTableClient = new Mock<TableClient>();
        var tenantId = Guid.NewGuid();
        var commandId = Guid.NewGuid();

        // Return empty result set (command not found)
        var asyncPageable = AsyncPageable<TableEntity>.FromPages(new[] 
        { 
            Page<TableEntity>.FromValues(Array.Empty<TableEntity>(), null, Mock.Of<Response>()) 
        });

        mockTableClient
            .Setup(x => x.QueryAsync<TableEntity>(
                It.IsAny<string>(), 
                It.IsAny<int?>(), 
                It.IsAny<IEnumerable<string>>(), 
                It.IsAny<CancellationToken>()))
            .Returns(asyncPageable);

        var mockSvc3 = new Mock<TableServiceClient>();
        mockSvc3.Setup(s => s.GetTableClient(It.IsAny<string>())).Returns(mockTableClient.Object);
        var store = new TableCommandQueueStore(mockSvc3.Object, _mockLogger.Object);

        // Act - should not throw
        await store.MarkAsSucceededAsync(tenantId, commandId, CancellationToken.None);

        // Assert - UpdateEntityAsync should never be called for non-existent entity
        mockTableClient.Verify(x => x.UpdateEntityAsync(
            It.IsAny<TableEntity>(),
            It.IsAny<ETag>(),
            It.IsAny<TableUpdateMode>(),
            It.IsAny<CancellationToken>()), 
            Times.Never);
    }

    [Fact]
    public async Task MarkAsProcessingAsync_ShouldIncrementAttemptCount()
    {
        // Arrange
        var mockTableClient = new Mock<TableClient>();
        var tenantId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        var partitionKey = tenantId.ToString().ToLowerInvariant();
        var rowKey = $"{commandId:N}_20260131120000000";

        var existingEntity = new TableEntity(partitionKey, rowKey)
        {
            ["CommandId"] = commandId.ToString(),
            ["Status"] = "Pending",
            ["DeviceId"] = Guid.NewGuid().ToString(),
            ["CommandName"] = "Test Command",
            ["CommandType"] = "REST",
            ["QueuedUtc"] = DateTimeOffset.UtcNow,
            ["QueuedByUserId"] = Guid.NewGuid().ToString(),
            ["AttemptCount"] = 0
        };

        var asyncPageable = AsyncPageable<TableEntity>.FromPages(new[] 
        { 
            Page<TableEntity>.FromValues(new[] { existingEntity }, null, Mock.Of<Response>()) 
        });

        mockTableClient
            .Setup(x => x.QueryAsync<TableEntity>(
                It.IsAny<string>(), 
                It.IsAny<int?>(), 
                It.IsAny<IEnumerable<string>>(), 
                It.IsAny<CancellationToken>()))
            .Returns(asyncPageable);

        mockTableClient
            .Setup(x => x.UpdateEntityAsync(
                It.Is<TableEntity>(e => 
                    e["Status"].ToString() == "Processing" && 
                    e["AttemptCount"].Equals(1) &&
                    e.ContainsKey("ProcessingStartedUtc")),
                It.IsAny<ETag>(),
                It.IsAny<TableUpdateMode>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(Mock.Of<Response>()));

        var mockSvc4 = new Mock<TableServiceClient>();
        mockSvc4.Setup(s => s.GetTableClient(It.IsAny<string>())).Returns(mockTableClient.Object);
        var store = new TableCommandQueueStore(mockSvc4.Object, _mockLogger.Object);

        // Act
        await store.MarkAsProcessingAsync(tenantId, commandId, CancellationToken.None);

        // Assert
        mockTableClient.Verify(x => x.UpdateEntityAsync(
            It.Is<TableEntity>(e => 
                e["Status"].ToString() == "Processing" && 
                e["AttemptCount"].Equals(1) &&
                e.ContainsKey("ProcessingStartedUtc")),
            It.IsAny<ETag>(),
            TableUpdateMode.Replace,
            It.IsAny<CancellationToken>()), 
            Times.Once);
    }
}
