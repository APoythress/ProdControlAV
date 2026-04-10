using System;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Moq;
using Xunit;
using ProdControlAV.Infrastructure.Services;

namespace ProdControlAV.Tests;

public class TableDeviceStatusStoreTests
{
    [Fact]
    public async Task UpsertAsync_UsesMergeMode()
    {
        // Arrange
        var mockTableClient = new Mock<TableClient>();
        var mockServiceClient = new Mock<TableServiceClient>();
        mockServiceClient
            .Setup(s => s.GetTableClient(It.IsAny<string>()))
            .Returns(mockTableClient.Object);
        var store = new TableDeviceStatusStore(mockServiceClient.Object);
        var tenantId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var status = "ONLINE";
        var latencyMs = 42;
        var timestamp = DateTimeOffset.UtcNow;

        TableEntity? capturedEntity = null;
        TableUpdateMode? capturedMode = null;

        mockTableClient
            .Setup(x => x.UpsertEntityAsync(
                It.IsAny<TableEntity>(), 
                It.IsAny<TableUpdateMode>(), 
                It.IsAny<CancellationToken>()))
            .Callback<TableEntity, TableUpdateMode, CancellationToken>((entity, mode, ct) =>
            {
                capturedEntity = entity;
                capturedMode = mode;
            })
            .ReturnsAsync(Mock.Of<Response>());

        // Act
        await store.UpsertAsync(tenantId, deviceId, status, latencyMs, timestamp, CancellationToken.None);

        // Assert
        mockTableClient.Verify(x => x.UpsertEntityAsync(
            It.IsAny<TableEntity>(), 
            TableUpdateMode.Merge, 
            CancellationToken.None), Times.Once);

        Assert.NotNull(capturedEntity);
        Assert.Equal(TableUpdateMode.Merge, capturedMode);
        Assert.Equal(tenantId.ToString().ToLowerInvariant(), capturedEntity.PartitionKey);
        Assert.Equal(deviceId.ToString(), capturedEntity.RowKey);
        Assert.Equal(status, capturedEntity["Status"]);
        Assert.Equal(latencyMs, capturedEntity["LatencyMs"]);
        Assert.Equal(timestamp, capturedEntity["LastSeenUtc"]);
    }

    [Fact]
    public async Task UpsertAsync_PreservesExistingColumns()
    {
        // Arrange
        var mockTableClient = new Mock<TableClient>();
        var mockServiceClient = new Mock<TableServiceClient>();
        mockServiceClient
            .Setup(s => s.GetTableClient(It.IsAny<string>()))
            .Returns(mockTableClient.Object);
        var store = new TableDeviceStatusStore(mockServiceClient.Object);
        var tenantId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();

        // Act
        await store.UpsertAsync(tenantId, deviceId, "ONLINE", 50, DateTimeOffset.UtcNow, CancellationToken.None);

        // Assert - Verify Merge mode is used, which preserves existing columns
        mockTableClient.Verify(x => x.UpsertEntityAsync(
            It.IsAny<TableEntity>(), 
            TableUpdateMode.Merge,  // Merge preserves existing columns
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
