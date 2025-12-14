using System;
using ProdControlAV.Infrastructure.Services;
using Xunit;

namespace ProdControlAV.Tests;

/// <summary>
/// Tests for command retry logic - focusing on DTO structure and defaults
/// </summary>
public class CommandRetryTests
{
    [Fact]
    public void CommandQueueDto_SupportsAttemptCount()
    {
        // Arrange & Act
        var dto = new CommandQueueDto(
            CommandId: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            DeviceId: Guid.NewGuid(),
            CommandName: "Test Command",
            CommandType: "REST",
            CommandData: "/test",
            HttpMethod: "GET",
            RequestBody: null,
            RequestHeaders: null,
            QueuedUtc: DateTimeOffset.UtcNow,
            QueuedByUserId: Guid.NewGuid(),
            DeviceIp: "192.168.1.1",
            DevicePort: 80,
            DeviceType: "Video",
            MonitorRecordingStatus: false,
            StatusEndpoint: null,
            StatusPollingIntervalSeconds: 60,
            Status: "Pending",
            AttemptCount: 2
        );
        
        // Assert
        Assert.Equal(2, dto.AttemptCount);
        Assert.Equal("Pending", dto.Status);
    }
    
    [Fact]
    public void CommandQueueDto_DefaultsAttemptCountToZero()
    {
        // Arrange & Act
        var dto = new CommandQueueDto(
            CommandId: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            DeviceId: Guid.NewGuid(),
            CommandName: "Test Command",
            CommandType: "REST",
            CommandData: "/test",
            HttpMethod: "GET",
            RequestBody: null,
            RequestHeaders: null,
            QueuedUtc: DateTimeOffset.UtcNow,
            QueuedByUserId: Guid.NewGuid()
        );
        
        // Assert
        Assert.Equal(0, dto.AttemptCount);
        Assert.Equal("Pending", dto.Status);
    }
    
    [Fact]
    public void CommandQueueDto_SupportsFailedStatus()
    {
        // Arrange & Act
        var dto = new CommandQueueDto(
            CommandId: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            DeviceId: Guid.NewGuid(),
            CommandName: "Test Command",
            CommandType: "REST",
            CommandData: "/test",
            HttpMethod: "GET",
            RequestBody: null,
            RequestHeaders: null,
            QueuedUtc: DateTimeOffset.UtcNow,
            QueuedByUserId: Guid.NewGuid(),
            DeviceIp: "192.168.1.1",
            DevicePort: 80,
            DeviceType: "Video",
            MonitorRecordingStatus: false,
            StatusEndpoint: null,
            StatusPollingIntervalSeconds: 60,
            Status: "Failed",
            AttemptCount: 3
        );
        
        // Assert
        Assert.Equal(3, dto.AttemptCount);
        Assert.Equal("Failed", dto.Status);
    }
    
    [Fact]
    public void CommandQueueDto_SupportsProcessingStatus()
    {
        // Arrange & Act
        var dto = new CommandQueueDto(
            CommandId: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            DeviceId: Guid.NewGuid(),
            CommandName: "Test Command",
            CommandType: "REST",
            CommandData: "/test",
            HttpMethod: "GET",
            RequestBody: null,
            RequestHeaders: null,
            QueuedUtc: DateTimeOffset.UtcNow,
            QueuedByUserId: Guid.NewGuid(),
            DeviceIp: "192.168.1.1",
            DevicePort: 80,
            DeviceType: "Video",
            MonitorRecordingStatus: false,
            StatusEndpoint: null,
            StatusPollingIntervalSeconds: 60,
            Status: "Processing",
            AttemptCount: 1
        );
        
        // Assert
        Assert.Equal(1, dto.AttemptCount);
        Assert.Equal("Processing", dto.Status);
    }
}
