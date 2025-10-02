using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using ProdControlAV.Agent.Services;
using ProdControlAV.Core.Models;
using Xunit;

namespace ProdControlAV.Tests;

public class DeviceSourceTests
{
    [Fact]
    public async Task RefreshAsync_WithValidResponse_UpdatesDeviceList()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<DeviceSource>>();
        var mockMessageHandler = new Mock<HttpMessageHandler>();
        
        var apiOptions = new ApiOptions
        {
            BaseUrl = "https://test.com/api",
            DevicesEndpoint = "/devices",
            StatusEndpoint = "/status",
            ApiKey = "test-key-12345678901234567890123456789012"
        };

        var deviceTargets = new List<DeviceTargetDto>
        {
            new DeviceTargetDto { Id = Guid.NewGuid(), IpAddress = "192.168.1.100", Type = "Camera", TcpPort = null },
            new DeviceTargetDto { Id = Guid.NewGuid(), IpAddress = "192.168.1.101", Type = "Switch", TcpPort = 23 }
        };

        var jsonResponse = JsonSerializer.Serialize(deviceTargets, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, System.Text.Encoding.UTF8, "application/json")
        };

        mockMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(httpResponse);

        var httpClient = new HttpClient(mockMessageHandler.Object);
        var mockJwtAuth = new Mock<IJwtAuthService>();
        mockJwtAuth.Setup(x => x.GetValidTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("valid-jwt-token");
        var deviceSource = new DeviceSource(httpClient, mockLogger.Object, Options.Create(apiOptions), mockJwtAuth.Object);

        // Act
        await deviceSource.RefreshAsync(CancellationToken.None);

        // Assert
        var devices = deviceSource.Current;
        Assert.Equal(2, devices.Count);
        
        var deviceList = new List<ProdControlAV.Agent.Models.Device>(devices);
        Assert.Equal("192.168.1.100", deviceList[0].Ip);
        Assert.False(deviceList[0].PreferTcp);
        Assert.Equal("192.168.1.101", deviceList[1].Ip);
        Assert.True(deviceList[1].PreferTcp);
    }

    [Fact]
    public async Task RefreshAsync_WithHttpError_LogsWarningAndDoesNotCrash()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<DeviceSource>>();
        var mockMessageHandler = new Mock<HttpMessageHandler>();
        
        var apiOptions = new ApiOptions
        {
            BaseUrl = "https://test.com/api",
            DevicesEndpoint = "/devices",
            StatusEndpoint = "/status",
            ApiKey = "test-key-12345678901234567890123456789012"
        };

        mockMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        var httpClient = new HttpClient(mockMessageHandler.Object);
        var mockJwtAuth = new Mock<IJwtAuthService>();
        mockJwtAuth.Setup(x => x.GetValidTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("valid-jwt-token");
        var deviceSource = new DeviceSource(httpClient, mockLogger.Object, Options.Create(apiOptions), mockJwtAuth.Object);

        // Act & Assert - Should not throw
        await deviceSource.RefreshAsync(CancellationToken.None);
        
        // Verify error was logged (HttpRequestException is now logged at Error level)
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("HTTP error while refreshing device list")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void Current_InitiallyEmpty()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<DeviceSource>>();
        var mockHttpClient = new Mock<HttpClient>();
        var mockJwtAuth = new Mock<IJwtAuthService>();
        
        var apiOptions = new ApiOptions
        {
            BaseUrl = "https://test.com/api",
            DevicesEndpoint = "/devices",
            StatusEndpoint = "/status",
            ApiKey = "test-key-12345678901234567890123456789012"
        };

        var deviceSource = new DeviceSource(mockHttpClient.Object, mockLogger.Object, Options.Create(apiOptions), mockJwtAuth.Object);

        // Act & Assert
        Assert.Empty(deviceSource.Current);
    }
}