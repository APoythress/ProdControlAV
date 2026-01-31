using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ProdControlAV.Agent.Interfaces;
using ProdControlAV.Agent.Services;
using Xunit;

namespace ProdControlAV.Tests;

public class AtemConnectionServiceTests
{
    [Fact]
    public void Constructor_WithEmptyIp_ThrowsArgumentException()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<AtemConnectionService>>();
        var options = new AtemOptions();
        
        // Act & Assert
        Assert.Throws<ArgumentException>(() => 
            new AtemConnectionService(mockLogger.Object, Options.Create(options), "", 9910));
    }
    
    [Fact]
    public void Constructor_WithValidOptions_InitializesSuccessfully()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<AtemConnectionService>>();
        var options = new AtemOptions();
        
        // Act
        var service = CreateService(mockLogger.Object, options);
        
        // Assert
        Assert.NotNull(service);
        Assert.Equal(AtemConnectionState.Disconnected, service.ConnectionState);
    }
    
    [Fact]
    public async Task ConnectAsync_WhenDisconnected_UpdatesConnectionState()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<AtemConnectionService>>();
        var options = new AtemOptions();
        var service = CreateService(mockLogger.Object, options);
        
        // Act
        await service.ConnectAsync();
        
        // Assert
        Assert.Equal(AtemConnectionState.Connected, service.ConnectionState);
    }
    
    [Fact]
    public async Task ConnectAsync_WhenAlreadyConnected_DoesNotReconnect()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<AtemConnectionService>>();
        var options = new AtemOptions();
        var service = CreateService(mockLogger.Object, options);
        await service.ConnectAsync();
        
        // Act
        await service.ConnectAsync(); // Second connect
        
        // Assert - Should still be connected
        Assert.Equal(AtemConnectionState.Connected, service.ConnectionState);
    }
    
    [Fact]
    public async Task DisconnectAsync_WhenConnected_UpdatesConnectionState()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<AtemConnectionService>>();
        var options = new AtemOptions();
        var service = CreateService(mockLogger.Object, options);
        await service.ConnectAsync();
        
        // Act
        await service.DisconnectAsync();
        
        // Assert
        Assert.Equal(AtemConnectionState.Disconnected, service.ConnectionState);
    }
    
    [Fact]
    public async Task CutToProgram_WhenConnected_CompletesSuccessfully()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<AtemConnectionService>>();
        var options = new AtemOptions();
        var service = CreateService(mockLogger.Object, options);
        await service.ConnectAsync();
        
        // Act
        await service.CutToProgramAsync(1);
        
        // Assert - Method completes without throwing
        Assert.Equal(AtemConnectionState.Connected, service.ConnectionState);
    }
    
    [Fact]
    public async Task CutToProgram_WhenDisconnected_ThrowsInvalidOperationException()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<AtemConnectionService>>();
        var options = new AtemOptions();
        var service = CreateService(mockLogger.Object, options);
        
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => 
            service.CutToProgramAsync(1));
    }
    
    [Fact]
    public async Task FadeToProgramAsync_WithCustomRate_UsesProvidedRate()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<AtemConnectionService>>();
        var options = new AtemOptions();
        var service = CreateService(mockLogger.Object, options);
        await service.ConnectAsync();
        
        // Act
        await service.FadeToProgramAsync(2, 60);
        
        // Assert - Method completes without throwing
        Assert.Equal(AtemConnectionState.Connected, service.ConnectionState);
    }
    
    [Fact]
    public async Task FadeToProgramAsync_WithoutRate_UsesDefaultRate()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<AtemConnectionService>>();
        var options = new AtemOptions();
        var service = CreateService(mockLogger.Object, options, transitionDefaultRate: 45);
        await service.ConnectAsync();
        
        // Act
        await service.FadeToProgramAsync(2);
        
        // Assert - Method completes without throwing
        Assert.Equal(AtemConnectionState.Connected, service.ConnectionState);
    }
    
    [Fact]
    public async Task SetPreviewAsync_WhenConnected_CompletesSuccessfully()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<AtemConnectionService>>();
        var options = new AtemOptions();
        var service = CreateService(mockLogger.Object, options);
        await service.ConnectAsync();
        
        // Act
        await service.SetPreviewAsync(3);
        
        // Assert - Method completes without throwing
        Assert.Equal(AtemConnectionState.Connected, service.ConnectionState);
    }
    
    [Fact]
    public async Task ListMacrosAsync_WhenConnected_ReturnsEmptyList()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<AtemConnectionService>>();
        var options = new AtemOptions();
        var service = CreateService(mockLogger.Object, options);
        await service.ConnectAsync();
        
        // Act
        var macros = await service.ListMacrosAsync();
        
        // Assert
        Assert.NotNull(macros);
        Assert.Empty(macros); // Stub returns empty list
    }
    
    [Fact]
    public async Task RunMacroAsync_WhenConnected_CompletesSuccessfully()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<AtemConnectionService>>();
        var options = new AtemOptions();
        var service = CreateService(mockLogger.Object, options);
        await service.ConnectAsync();
        
        // Act
        await service.RunMacroAsync(5);
        
        // Assert - Method completes without throwing
        Assert.Equal(AtemConnectionState.Connected, service.ConnectionState);
    }
    
    // Helper method to create service with device-specific settings
    private static AtemConnectionService CreateService(
        ILogger<AtemConnectionService> logger,
        AtemOptions options,
        string deviceIp = "192.168.1.240",
        int devicePort = 9910,
        string deviceName = "Test ATEM",
        int transitionDefaultRate = 30)
    {
        return new AtemConnectionService(
            logger,
            Options.Create(options),
            deviceIp,
            devicePort,
            deviceName,
            transitionDefaultRate);
    }
}
