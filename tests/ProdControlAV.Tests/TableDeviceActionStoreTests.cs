using System;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Moq;
using Xunit;
using ProdControlAV.Infrastructure.Services;

namespace ProdControlAV.Tests;

public class TableDeviceActionStoreTests
{
    [Fact]
    public async Task UpsertAsync_UsesMergeMode()
    {
        // Arrange
        var mockTableClient = new Mock<TableClient>();
        var store = new TableDeviceActionStore(mockTableClient.Object);
        var tenantId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var actionName = "PowerOn";

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
        await store.UpsertAsync(tenantId, actionId, deviceId, actionName, CancellationToken.None);

        // Assert
        mockTableClient.Verify(x => x.UpsertEntityAsync(
            It.IsAny<TableEntity>(), 
            TableUpdateMode.Merge, 
            CancellationToken.None), Times.Once);

        Assert.NotNull(capturedEntity);
        Assert.Equal(TableUpdateMode.Merge, capturedMode);
        Assert.Equal(tenantId.ToString().ToLowerInvariant(), capturedEntity.PartitionKey);
        Assert.Equal(actionId.ToString(), capturedEntity.RowKey);
        Assert.Equal(deviceId.ToString(), capturedEntity["DeviceId"]);
        Assert.Equal(actionName, capturedEntity["ActionName"]);
    }

    [Fact]
    public async Task UpsertAsync_PreservesExistingColumns()
    {
        // Arrange
        var mockTableClient = new Mock<TableClient>();
        var store = new TableDeviceActionStore(mockTableClient.Object);
        var tenantId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();

        // Act
        await store.UpsertAsync(tenantId, actionId, deviceId, "PowerOff", CancellationToken.None);

        // Assert - Verify Merge mode is used, which preserves existing columns
        mockTableClient.Verify(x => x.UpsertEntityAsync(
            It.IsAny<TableEntity>(), 
            TableUpdateMode.Merge,  // Merge preserves existing columns
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
