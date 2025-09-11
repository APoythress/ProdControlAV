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
using Xunit;

namespace ProdControlAV.Tests;

public class StatusPublisherTests
{
    [Fact]
    public async Task PublishAsync_WithValidStatus_SendsCorrectRequest()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<StatusPublisher>>();
        var mockMessageHandler = new Mock<HttpMessageHandler>();
        
        var apiOptions = new ApiOptions
        {
            BaseUrl = "https://test.com/api",
            DevicesEndpoint = "/devices",
            StatusEndpoint = "/status",
            ApiKey = "test-key-12345678901234567890123456789012"
        };

        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"ok\":true}", System.Text.Encoding.UTF8, "application/json")
        };

        mockMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(httpResponse);

        var httpClient = new HttpClient(mockMessageHandler.Object);
        var statusPublisher = new StatusPublisher(httpClient, mockLogger.Object, Options.Create(apiOptions));

        var deviceStatus = new DeviceStatus("device1", "Test Device", "192.168.1.100", "Online", DateTimeOffset.UtcNow);

        // Act
        await statusPublisher.PublishAsync(deviceStatus, CancellationToken.None);

        // Assert
        mockMessageHandler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Post &&
                req.RequestUri!.ToString().Contains("/status")),
            ItExpr.IsAny<CancellationToken>()
        );
    }

    [Fact]
    public async Task HeartbeatAsync_WithDeviceSnapshot_SendsHeartbeat()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<StatusPublisher>>();
        var mockMessageHandler = new Mock<HttpMessageHandler>();
        
        var apiOptions = new ApiOptions
        {
            BaseUrl = "https://test.com/api",
            DevicesEndpoint = "/devices",
            StatusEndpoint = "/status",
            HeartbeatEndpoint = "/heartbeat",
            ApiKey = "test-key-12345678901234567890123456789012"
        };

        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"ok\":true}", System.Text.Encoding.UTF8, "application/json")
        };

        mockMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(httpResponse);

        var httpClient = new HttpClient(mockMessageHandler.Object);
        var statusPublisher = new StatusPublisher(httpClient, mockLogger.Object, Options.Create(apiOptions));

        var deviceStatuses = new List<DeviceStatus>
        {
            new DeviceStatus("device1", "Test Device 1", "192.168.1.100", "Online", DateTimeOffset.UtcNow),
            new DeviceStatus("device2", "Test Device 2", "192.168.1.101", "Offline", DateTimeOffset.UtcNow)
        };

        // Act
        await statusPublisher.HeartbeatAsync(deviceStatuses, CancellationToken.None);

        // Assert
        mockMessageHandler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Post &&
                req.RequestUri!.ToString().Contains("/heartbeat")),
            ItExpr.IsAny<CancellationToken>()
        );
    }

    [Fact]
    public async Task PublishAsync_WithHttpError_LogsWarningAndDoesNotThrow()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<StatusPublisher>>();
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
        var statusPublisher = new StatusPublisher(httpClient, mockLogger.Object, Options.Create(apiOptions));

        var deviceStatus = new DeviceStatus("device1", "Test Device", "192.168.1.100", "Online", DateTimeOffset.UtcNow);

        // Act & Assert - Should not throw
        await statusPublisher.PublishAsync(deviceStatus, CancellationToken.None);
        
        // Verify warning was logged
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to publish")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task HeartbeatAsync_WithNullHeartbeatEndpoint_DoesNotSendRequest()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<StatusPublisher>>();
        var mockMessageHandler = new Mock<HttpMessageHandler>();
        
        var apiOptions = new ApiOptions
        {
            BaseUrl = "https://test.com/api",
            DevicesEndpoint = "/devices",
            StatusEndpoint = "/status",
            HeartbeatEndpoint = null, // No heartbeat endpoint configured
            ApiKey = "test-key-12345678901234567890123456789012"
        };

        var httpClient = new HttpClient(mockMessageHandler.Object);
        var statusPublisher = new StatusPublisher(httpClient, mockLogger.Object, Options.Create(apiOptions));

        var deviceStatuses = new List<DeviceStatus>
        {
            new DeviceStatus("device1", "Test Device", "192.168.1.100", "Online", DateTimeOffset.UtcNow)
        };

        // Act
        await statusPublisher.HeartbeatAsync(deviceStatuses, CancellationToken.None);

        // Assert - No HTTP request should be made
        mockMessageHandler.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>()
        );
    }
}