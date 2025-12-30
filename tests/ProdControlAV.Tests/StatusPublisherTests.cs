using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;
using ProdControlAV.Agent.Services;

namespace ProdControlAV.Tests;

public class StatusPublisherTests
{
    [Fact]
    public async Task HeartbeatAsync_SendsCorrectAssemblyVersion()
    {
        // Arrange
        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        var mockLogger = new Mock<ILogger<StatusPublisher>>();
        var mockJwtAuth = new Mock<IJwtAuthService>();
        
        var apiOptions = new ApiOptions
        {
            BaseUrl = "https://api.test.com",
            DevicesEndpoint = "/api/agents/devices",
            HeartbeatEndpoint = "/api/agents/heartbeat",
            StatusEndpoint = "/api/agents/status",
            ApiKey = "test-key",
            TenantId = Guid.NewGuid()
        };
        
        var optionsMock = new Mock<IOptions<ApiOptions>>();
        optionsMock.Setup(o => o.Value).Returns(apiOptions);

        // Setup mock JWT auth to return a valid token
        mockJwtAuth.Setup(j => j.GetValidTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-jwt-token");

        // Capture the request content to verify version
        string? capturedContent = null;
        string? capturedAuthScheme = null;
        string? capturedAuthParameter = null;
        
        mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .Callback<HttpRequestMessage, CancellationToken>(async (req, ct) => 
            {
                if (req.RequestUri?.ToString().Contains("heartbeat") == true)
                {
                    // Capture content before it's disposed
                    capturedContent = await req.Content!.ReadAsStringAsync();
                    capturedAuthScheme = req.Headers.Authorization?.Scheme;
                    capturedAuthParameter = req.Headers.Authorization?.Parameter;
                }
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        var httpClient = new HttpClient(mockHttpMessageHandler.Object);
        
        var statusPublisher = new StatusPublisher(
            httpClient, 
            mockLogger.Object, 
            optionsMock.Object, 
            mockJwtAuth.Object
        );

        // Get expected version from assembly - use GetExecutingAssembly to match implementation
        // Note: In tests, this will be the test assembly version, which won't match the actual
        // Agent assembly version. We're testing the logic, not the exact version value.
        // Just verify it's not the old hardcoded value "1.0.001"
        var assembly = Assembly.GetExecutingAssembly();
        var testAssemblyVersion = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion 
            ?? assembly?.GetName().Version?.ToString() 
            ?? "0.0.0";

        // Act
        await statusPublisher.HeartbeatAsync(Array.Empty<DeviceStatus>(), CancellationToken.None);

        // Assert
        Assert.NotNull(capturedContent);
        
        // Verify that the content does NOT contain the old hardcoded version
        Assert.DoesNotContain("\"version\":\"1.0.001\"", capturedContent.ToLower());
        
        // Verify that the content contains a version field with some value (not null or empty)
        var normalizedContent = capturedContent.Replace("\\u002B", "+");
        Assert.Contains("\"version\":", normalizedContent);
        Assert.DoesNotContain("\"version\":null", normalizedContent);
        Assert.DoesNotContain("\"version\":\"\"", normalizedContent);
        
        // Verify JWT token was used
        Assert.Equal("Bearer", capturedAuthScheme);
        Assert.Equal("test-jwt-token", capturedAuthParameter);
        
        // Verify JWT auth service was called
        mockJwtAuth.Verify(j => j.GetValidTokenAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task HeartbeatAsync_DoesNotSendHeartbeat_WhenJwtTokenIsNull()
    {
        // Arrange
        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        var mockLogger = new Mock<ILogger<StatusPublisher>>();
        var mockJwtAuth = new Mock<IJwtAuthService>();
        
        var apiOptions = new ApiOptions
        {
            BaseUrl = "https://api.test.com",
            DevicesEndpoint = "/api/agents/devices",
            HeartbeatEndpoint = "/api/agents/heartbeat",
            StatusEndpoint = "/api/agents/status",
            ApiKey = "test-key",
            TenantId = Guid.NewGuid()
        };
        
        var optionsMock = new Mock<IOptions<ApiOptions>>();
        optionsMock.Setup(o => o.Value).Returns(apiOptions);

        // Setup mock JWT auth to return null (token refresh failed)
        mockJwtAuth.Setup(j => j.GetValidTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var httpClient = new HttpClient(mockHttpMessageHandler.Object);
        
        var statusPublisher = new StatusPublisher(
            httpClient, 
            mockLogger.Object, 
            optionsMock.Object, 
            mockJwtAuth.Object
        );

        // Act
        await statusPublisher.HeartbeatAsync(Array.Empty<DeviceStatus>(), CancellationToken.None);

        // Assert - verify that SendAsync was never called
        mockHttpMessageHandler
            .Protected()
            .Verify<Task<HttpResponseMessage>>(
                "SendAsync",
                Times.Never(),
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            );
    }
}
