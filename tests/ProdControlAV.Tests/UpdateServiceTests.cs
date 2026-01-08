using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ProdControlAV.Agent.Services;
using System.Reflection;
using Xunit;

namespace ProdControlAV.Tests;

public class UpdateServiceTests
{
    [Theory]
    [InlineData("1.0.51+8754e5aea7b046f17bf019c21a5e362da589f224", "1.0.51")]
    [InlineData("1.0.0+abc123", "1.0.0")]
    [InlineData("2.5.3+build.2024", "2.5.3")]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("1.0.0-beta+build", "1.0.0-beta")]
    [InlineData("", "0.0.0")]
    [InlineData(null, "0.0.0")]
    public void StripBuildMetadata_HandlesVariousVersionFormats(string? input, string expected)
    {
        // Arrange
        var method = typeof(UpdateService).GetMethod("StripBuildMetadata", 
            BindingFlags.NonPublic | BindingFlags.Static);
        
        Assert.NotNull(method);
        
        // Act
        var result = method.Invoke(null, new object?[] { input });
        
        // Assert
        Assert.Equal(expected, result);
    }
    
    [Fact]
    public void Constructor_StripsVersionBuildMetadata()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<UpdateService>>();
        var updateOptions = Options.Create(new UpdateOptions
        {
            Enabled = false, // Don't actually start the service
            AppcastUrl = "https://example.com/appcast.json",
            Ed25519PublicKey = "test-key"
        });
        
        // Act
        var service = new UpdateService(mockLogger.Object, updateOptions);
        
        // Assert
        // The service should be constructed successfully
        Assert.NotNull(service);
        
        // Verify that the service doesn't crash with version metadata
        // The actual version stripping is tested by the StripBuildMetadata test
    }
}
