using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using ProdControlAV.API.Controllers;
using ProdControlAV.Infrastructure.Services;

public class StatusControllerTests
{
    [Fact]
    public async Task Post_ValidTenantClaim_UpsertsStatus()
    {
        var statusStoreMock = new Mock<IDeviceStatusStore>();
        var deviceStoreMock = new Mock<IDeviceStore>();
        var loggerMock = new Mock<ILogger<StatusController>>();
        var controller = new StatusController(statusStoreMock.Object, deviceStoreMock.Object, loggerMock.Object);
        var tenantId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("tenant_id", tenantId.ToString()) }, "mock"));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } };
        var dto = new StatusPostDto(tenantId, deviceId, "Online", 10, DateTimeOffset.UtcNow);
        var result = await controller.Post(dto, CancellationToken.None);
        statusStoreMock.Verify(s => s.UpsertAsync(tenantId, deviceId, "Online", 10, It.IsAny<DateTimeOffset>(), CancellationToken.None), Times.Once);
        deviceStoreMock.Verify(s => s.UpsertStatusAsync(tenantId, deviceId, "Online", It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), CancellationToken.None), Times.Once);
        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Post_InvalidTenantClaim_ReturnsForbid()
    {
        var statusStoreMock = new Mock<IDeviceStatusStore>();
        var deviceStoreMock = new Mock<IDeviceStore>();
        var loggerMock = new Mock<ILogger<StatusController>>();
        var controller = new StatusController(statusStoreMock.Object, deviceStoreMock.Object, loggerMock.Object);
        var tenantId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("tenant_id", Guid.NewGuid().ToString()) }, "mock"));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } };
        var dto = new StatusPostDto(tenantId, deviceId, "Online", 10, DateTimeOffset.UtcNow);
        var result = await controller.Post(dto, CancellationToken.None);
        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Get_ValidTenantClaim_ReturnsStatusList()
    {
        var tenantId = Guid.NewGuid();
        var statusStoreMock = new Mock<IDeviceStatusStore>();
        var deviceStoreMock = new Mock<IDeviceStore>();
        var loggerMock = new Mock<ILogger<StatusController>>();
        var controller = new StatusController(statusStoreMock.Object, deviceStoreMock.Object, loggerMock.Object);
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("tenant_id", tenantId.ToString()) }, "mock"));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } };
        var dtos = new List<DeviceStatusDto> { new DeviceStatusDto(Guid.NewGuid(), "Online", 10, DateTimeOffset.UtcNow) };
        statusStoreMock.Setup(s => s.GetAllForTenantAsync(tenantId, CancellationToken.None)).Returns(GetAsyncEnumerable(dtos));
        var result = await controller.Get(tenantId, CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var statusList = Assert.IsType<StatusListDto>(okResult.Value);
        Assert.Equal(tenantId, statusList.TenantId);
        Assert.Single(statusList.Items);
    }

    private static async IAsyncEnumerable<DeviceStatusDto> GetAsyncEnumerable(IEnumerable<DeviceStatusDto> dtos)
    {
        foreach (var d in dtos)
            yield return d;
        await Task.CompletedTask;
    }
}
