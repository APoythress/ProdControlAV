using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Moq;
using Xunit;
using ProdControlAV.Infrastructure.Services;

namespace ProdControlAV.Tests;

public class TableDeviceStoreTests
{
    [Fact]
    public async Task UpsertStatusAsync_UsesMergeMode()
    {
        // Arrange
        var mockTableClient = new Mock<TableClient>();
        var store = new TableDeviceStore(mockTableClient.Object);
        var tenantId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var status = "Online";
        var lastSeen = DateTimeOffset.UtcNow;
        var lastPolled = DateTimeOffset.UtcNow.AddSeconds(-5);

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
        await store.UpsertStatusAsync(tenantId, deviceId, status, lastSeen, lastPolled, CancellationToken.None);

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
        Assert.Equal(lastSeen, capturedEntity["LastSeenUtc"]);
        Assert.Equal(lastPolled, capturedEntity["LastPolledUtc"]);
    }

    [Fact]
    public async Task UpsertAsync_UsesReplaceMode()
    {
        // Arrange
        var mockTableClient = new Mock<TableClient>();
        var store = new TableDeviceStore(mockTableClient.Object);
        var tenantId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();

        TableUpdateMode? capturedMode = null;

        mockTableClient
            .Setup(x => x.UpsertEntityAsync(
                It.IsAny<TableEntity>(), 
                It.IsAny<TableUpdateMode>(), 
                It.IsAny<CancellationToken>()))
            .Callback<TableEntity, TableUpdateMode, CancellationToken>((entity, mode, ct) =>
            {
                capturedMode = mode;
            })
            .ReturnsAsync(Mock.Of<Response>());

        // Act
        await store.UpsertAsync(
            tenantId, deviceId, "Device1", "192.168.1.1", "Camera", 
            DateTimeOffset.UtcNow, "Model1", "Brand1", "Location1", 
            false, 80, CancellationToken.None);

        // Assert
        mockTableClient.Verify(x => x.UpsertEntityAsync(
            It.IsAny<TableEntity>(), 
            TableUpdateMode.Replace, 
            CancellationToken.None), Times.Once);

        Assert.Equal(TableUpdateMode.Replace, capturedMode);
    }

    [Fact]
    public async Task GetAllForTenantAsync_ReturnsDevicesWithStatusFields()
    {
        // Arrange
        var mockTableClient = new Mock<TableClient>();
        var store = new TableDeviceStore(mockTableClient.Object);
        var tenantId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();

        var entities = new List<TableEntity>
        {
            new TableEntity(tenantId.ToString().ToLowerInvariant(), deviceId.ToString())
            {
                ["Name"] = "Device1",
                ["IpAddress"] = "192.168.1.1",
                ["Type"] = "Camera",
                ["CreatedUtc"] = DateTimeOffset.UtcNow,
                ["Model"] = "Model1",
                ["Brand"] = "Brand1",
                ["Location"] = "Location1",
                ["AllowTelNet"] = false,
                ["Port"] = 80,
                ["Status"] = "Online",
                ["LastSeenUtc"] = DateTimeOffset.UtcNow,
                ["LastPolledUtc"] = DateTimeOffset.UtcNow.AddSeconds(-5),
                ["HealthMetric"] = 0.95
            }
        };

        var mockPages = CreateAsyncPageable(entities);

        mockTableClient
            .Setup(x => x.QueryAsync<TableEntity>(
                It.IsAny<System.Linq.Expressions.Expression<Func<TableEntity, bool>>>(), 
                It.IsAny<int?>(), 
                It.IsAny<IEnumerable<string>>(), 
                It.IsAny<CancellationToken>()))
            .Returns(mockPages);

        // Act
        var results = new List<DeviceDto>();
        await foreach (var device in store.GetAllForTenantAsync(tenantId, CancellationToken.None))
        {
            results.Add(device);
        }

        // Assert
        Assert.Single(results);
        var device1 = results[0];
        Assert.Equal(deviceId, device1.Id);
        Assert.Equal("Device1", device1.Name);
        Assert.Equal("Online", device1.Status);
        Assert.NotNull(device1.LastSeenUtc);
        Assert.NotNull(device1.LastPolledUtc);
        Assert.Equal(0.95, device1.HealthMetric);
    }

    private static AsyncPageable<TableEntity> CreateAsyncPageable(IEnumerable<TableEntity> entities)
    {
        var mockPageable = new Mock<AsyncPageable<TableEntity>>();
        mockPageable
            .Setup(x => x.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns(GetAsyncEnumerator(entities));
        return mockPageable.Object;
    }

    private static async IAsyncEnumerator<TableEntity> GetAsyncEnumerator(IEnumerable<TableEntity> entities)
    {
        foreach (var entity in entities)
        {
            yield return entity;
        }
        await Task.CompletedTask;
    }
}
