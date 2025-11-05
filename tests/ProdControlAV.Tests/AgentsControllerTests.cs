using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using ProdControlAV.API.Controllers;
using ProdControlAV.API.Services;
using ProdControlAV.Core.Interfaces;
using ProdControlAV.Core.Models;
using ProdControlAV.Infrastructure.Services;

namespace ProdControlAV.Tests;

public class AgentsControllerTests
{
    [Fact]
    public async Task GetDevices_UsesTableStorage_NotSql()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();

        // GetDevices doesn't use AppDbContext, so we can pass null
        var authMock = new Mock<IAgentAuth>();
        var jwtMock = new Mock<IJwtService>();
        var loggerMock = new Mock<ILogger<AgentsController>>();
        var queueMock = new Mock<IAgentCommandQueueService>();
        var statusStoreMock = new Mock<IDeviceStatusStore>();
        var deviceStoreMock = new Mock<IDeviceStore>();
        var activityMonitorMock = new Mock<IActivityMonitor>();

        // Setup device store to return a device from Table Storage
        var devices = new List<DeviceDto>
        {
            new DeviceDto(
                Id: deviceId,
                Name: "Test Device",
                IpAddress: "192.168.1.100",
                Type: "Camera",
                TenantId: tenantId,
                CreatedUtc: DateTimeOffset.UtcNow,
                Model: "TestModel",
                Brand: "TestBrand",
                Location: "Office",
                AllowTelNet: false,
                Port: 80
            )
        };

        deviceStoreMock.Setup(s => s.GetAllForTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .Returns(GetAsyncEnumerable(devices));

        var controller = new AgentsController(
            null!, // GetDevices doesn't use AppDbContext
            authMock.Object,
            jwtMock.Object,
            loggerMock.Object,
            queueMock.Object,
            statusStoreMock.Object,
            deviceStoreMock.Object,
            activityMonitorMock.Object
        );

        // Setup JWT claims
        var claims = new[]
        {
            new Claim("sub", agentId.ToString()),
            new Claim("tenantId", tenantId.ToString())
        };
        var identity = new ClaimsIdentity(claims, "mock");
        var user = new ClaimsPrincipal(identity);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        // Act
        var result = await controller.GetDevices(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var deviceList = Assert.IsType<List<DeviceTargetDto>>(okResult.Value);
        Assert.Single(deviceList);
        Assert.Equal(deviceId, deviceList[0].Id);
        Assert.Equal("192.168.1.100", deviceList[0].IpAddress);
        Assert.Equal("Camera", deviceList[0].Type);
        Assert.Equal(300, deviceList[0].PingFrequencySeconds); // Default value

        // Verify Table Storage was called
        deviceStoreMock.Verify(s => s.GetAllForTenantAsync(tenantId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetDevices_InvalidClaims_ReturnsUnauthorized()
    {
        // Arrange
        var authMock = new Mock<IAgentAuth>();
        var jwtMock = new Mock<IJwtService>();
        var loggerMock = new Mock<ILogger<AgentsController>>();
        var queueMock = new Mock<IAgentCommandQueueService>();
        var statusStoreMock = new Mock<IDeviceStatusStore>();
        var deviceStoreMock = new Mock<IDeviceStore>();
        var activityMonitorMock = new Mock<IActivityMonitor>();

        var controller = new AgentsController(
            null!, // GetDevices doesn't use AppDbContext
            authMock.Object,
            jwtMock.Object,
            loggerMock.Object,
            queueMock.Object,
            statusStoreMock.Object,
            deviceStoreMock.Object,
            activityMonitorMock.Object
        );

        // Setup invalid claims (missing tenantId)
        var claims = new[]
        {
            new Claim("sub", Guid.NewGuid().ToString())
        };
        var identity = new ClaimsIdentity(claims, "mock");
        var user = new ClaimsPrincipal(identity);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        // Act
        var result = await controller.GetDevices(CancellationToken.None);

        // Assert
        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        var error = unauthorizedResult.Value;
        Assert.NotNull(error);
    }

    [Fact]
    public async Task GetDevices_MultipleDevices_ReturnsAll()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        var authMock = new Mock<IAgentAuth>();
        var jwtMock = new Mock<IJwtService>();
        var loggerMock = new Mock<ILogger<AgentsController>>();
        var queueMock = new Mock<IAgentCommandQueueService>();
        var statusStoreMock = new Mock<IDeviceStatusStore>();
        var deviceStoreMock = new Mock<IDeviceStore>();
        var activityMonitorMock = new Mock<IActivityMonitor>();

        // Setup multiple devices
        var devices = new List<DeviceDto>
        {
            new DeviceDto(Guid.NewGuid(), "Device1", "192.168.1.1", "Camera", tenantId, DateTimeOffset.UtcNow, null, null, null, false, 80),
            new DeviceDto(Guid.NewGuid(), "Device2", "192.168.1.2", "Display", tenantId, DateTimeOffset.UtcNow, null, null, null, false, 80),
            new DeviceDto(Guid.NewGuid(), "Device3", "192.168.1.3", "Processor", tenantId, DateTimeOffset.UtcNow, null, null, null, false, 80)
        };

        deviceStoreMock.Setup(s => s.GetAllForTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .Returns(GetAsyncEnumerable(devices));

        var controller = new AgentsController(
            null!, // GetDevices doesn't use AppDbContext
            authMock.Object,
            jwtMock.Object,
            loggerMock.Object,
            queueMock.Object,
            statusStoreMock.Object,
            deviceStoreMock.Object,
            activityMonitorMock.Object
        );

        // Setup JWT claims
        var claims = new[]
        {
            new Claim("sub", agentId.ToString()),
            new Claim("tenantId", tenantId.ToString())
        };
        var identity = new ClaimsIdentity(claims, "mock");
        var user = new ClaimsPrincipal(identity);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        // Act
        var result = await controller.GetDevices(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var deviceList = Assert.IsType<List<DeviceTargetDto>>(okResult.Value);
        Assert.Equal(3, deviceList.Count);
        Assert.Equal("192.168.1.1", deviceList[0].IpAddress);
        Assert.Equal("192.168.1.2", deviceList[1].IpAddress);
        Assert.Equal("192.168.1.3", deviceList[2].IpAddress);

        // Verify Table Storage was called once
        deviceStoreMock.Verify(s => s.GetAllForTenantAsync(tenantId, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static async IAsyncEnumerable<DeviceDto> GetAsyncEnumerable(IEnumerable<DeviceDto> dtos)
    {
        foreach (var d in dtos)
            yield return d;
        await Task.CompletedTask;
    }
}
