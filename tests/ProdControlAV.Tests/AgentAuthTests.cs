using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using ProdControlAV.API.Services;
using ProdControlAV.Core.Models;
using ProdControlAV.Infrastructure.Services;
using Xunit;
using AgentModel = ProdControlAV.Core.Models.Agent;

namespace ProdControlAV.Tests;

public class AgentAuthTests
{
    [Fact]
    public async Task ValidateAsync_LogsComputedHash_AtDebugLevel()
    {
        // Arrange
        var mockAuthStore = new Mock<IAgentAuthStore>();
        var mockLogger = new Mock<ILogger<AgentAuth>>();
        var dbContext = CreateInMemoryDbContext();
        
        mockAuthStore
            .Setup(x => x.ValidateAgentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentAuthDto?)null);
        
        var agentAuth = new AgentAuth(mockAuthStore.Object, dbContext, mockLogger.Object);
        var testAgentKey = "test-agent-key-12345";

        // Act
        await agentAuth.ValidateAsync(testAgentKey, CancellationToken.None);

        // Assert - Verify Debug log was called with the computed hash
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Computed agent key hash for incoming agent")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        
        // Verify that the raw agent key is NOT logged
        mockLogger.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(testAgentKey)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never,
            "Raw agent key should never be logged");
    }

    [Fact]
    public void HashAgentKey_ProducesUppercaseHexString()
    {
        // Arrange
        var mockAuthStore = new Mock<IAgentAuthStore>();
        var mockLogger = new Mock<ILogger<AgentAuth>>();
        var dbContext = CreateInMemoryDbContext();
        var agentAuth = new AgentAuth(mockAuthStore.Object, dbContext, mockLogger.Object);
        var testKey = "test-key";

        // Act
        var hash = agentAuth.HashAgentKey(testKey);

        // Assert
        Assert.NotEmpty(hash);
        Assert.Equal(64, hash.Length); // SHA256 produces 32 bytes = 64 hex chars
        Assert.Matches("^[A-F0-9]+$", hash); // Should be uppercase hex
    }

    [Fact]
    public async Task ValidateAsync_WithEmptyKey_ReturnsErrorWithoutLogging()
    {
        // Arrange
        var mockAuthStore = new Mock<IAgentAuthStore>();
        var mockLogger = new Mock<ILogger<AgentAuth>>();
        var dbContext = CreateInMemoryDbContext();
        var agentAuth = new AgentAuth(mockAuthStore.Object, dbContext, mockLogger.Object);

        // Act
        var (agent, error) = await agentAuth.ValidateAsync("", CancellationToken.None);

        // Assert
        Assert.Null(agent);
        Assert.Equal("missing_agent_key", error);
        
        // Verify no logging occurred for empty key
        mockLogger.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task ValidateAsync_FallsBackToDatabase_WhenNotInTableStore()
    {
        // Arrange
        var mockAuthStore = new Mock<IAgentAuthStore>();
        var mockLogger = new Mock<ILogger<AgentAuth>>();
        var dbContext = CreateInMemoryDbContext();
        
        var agentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var testAgentKey = "test-key";
        
        var agentAuth = new AgentAuth(mockAuthStore.Object, dbContext, mockLogger.Object);
        var hash = agentAuth.HashAgentKey(testAgentKey);
        
        // Setup: First lookup returns null, verification lookup returns the agent (after sync)
        var lookupCount = 0;
        mockAuthStore
            .Setup(x => x.ValidateAgentAsync(hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                lookupCount++;
                if (lookupCount == 1) return null; // Initial lookup fails
                // Verification lookup succeeds
                return new AgentAuthDto(
                    AgentId: agentId,
                    TenantId: tenantId,
                    Name: "Test Agent",
                    AgentKeyHash: hash,
                    LastHostname: "test-host",
                    LastIp: "192.168.1.1",
                    LastSeenUtc: DateTimeOffset.UtcNow,
                    Version: "1.0.0"
                );
            });
        
        // Setup: UpsertAgentAsync succeeds
        mockAuthStore
            .Setup(x => x.UpsertAgentAsync(It.IsAny<AgentAuthDto>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        
        // Create agent in database
        var dbAgent = new AgentModel
        {
            Id = agentId,
            TenantId = tenantId,
            Name = "Test Agent",
            AgentKeyHash = hash,
            LastHostname = "test-host",
            LastIp = "192.168.1.1",
            LastSeenUtc = DateTime.UtcNow,
            Version = "1.0.0"
        };
        
        dbContext.Agents.Add(dbAgent);
        await dbContext.SaveChangesAsync();
        
        // Act
        var (agent, error) = await agentAuth.ValidateAsync(testAgentKey, CancellationToken.None);
        
        // Assert
        Assert.NotNull(agent);
        Assert.Null(error);
        Assert.Equal(agentId, agent.Id);
        Assert.Equal(tenantId, agent.TenantId);
        Assert.Equal("Test Agent", agent.Name);
        
        // Verify that UpsertAgentAsync was called to sync to table store
        mockAuthStore.Verify(
            x => x.UpsertAgentAsync(
                It.Is<AgentAuthDto>(dto => 
                    dto.AgentId == agentId && 
                    dto.TenantId == tenantId &&
                    dto.AgentKeyHash == hash),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "Agent should be synced to table store after database lookup");
        
        // Verify that ValidateAgentAsync was called twice (initial + verification)
        mockAuthStore.Verify(
            x => x.ValidateAgentAsync(hash, It.IsAny<CancellationToken>()),
            Times.Exactly(2),
            "Should lookup twice: initial + verification after sync");
        
        // Verify appropriate logs
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Agent not found in Table Store")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Agent found in SQL database")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ValidateAsync_ReturnsError_WhenNotInTableStoreOrDatabase()
    {
        // Arrange
        var mockAuthStore = new Mock<IAgentAuthStore>();
        var mockLogger = new Mock<ILogger<AgentAuth>>();
        var dbContext = CreateInMemoryDbContext();
        
        var testAgentKey = "non-existent-key";
        
        // Table store returns null (agent not found)
        mockAuthStore
            .Setup(x => x.ValidateAgentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentAuthDto?)null);
        
        var agentAuth = new AgentAuth(mockAuthStore.Object, dbContext, mockLogger.Object);
        
        // Act
        var (agent, error) = await agentAuth.ValidateAsync(testAgentKey, CancellationToken.None);
        
        // Assert
        Assert.Null(agent);
        Assert.Equal("invalid_agent_key", error);
        
        // Verify UpsertAgentAsync was NOT called since agent not in DB
        mockAuthStore.Verify(
            x => x.UpsertAgentAsync(It.IsAny<AgentAuthDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
        
        // Verify appropriate logs
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Agent key hash not found in database")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ValidateAsync_ReturnsFatalError_WhenTableStoreSyncFails()
    {
        // Arrange
        var mockAuthStore = new Mock<IAgentAuthStore>();
        var mockLogger = new Mock<ILogger<AgentAuth>>();
        var dbContext = CreateInMemoryDbContext();
        
        var agentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var testAgentKey = "test-key-sync-fail";
        
        var agentAuth = new AgentAuth(mockAuthStore.Object, dbContext, mockLogger.Object);
        var hash = agentAuth.HashAgentKey(testAgentKey);
        
        // Setup: Table store returns null initially (agent not in table store)
        mockAuthStore
            .Setup(x => x.ValidateAgentAsync(hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentAuthDto?)null);
        
        // Setup: UpsertAgentAsync fails with exception
        mockAuthStore
            .Setup(x => x.UpsertAgentAsync(It.IsAny<AgentAuthDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Table Storage unavailable"));
        
        // Create agent in database
        var dbAgent = new AgentModel
        {
            Id = agentId,
            TenantId = tenantId,
            Name = "Test Agent",
            AgentKeyHash = hash,
            LastHostname = "test-host",
            LastIp = "192.168.1.1",
            LastSeenUtc = DateTime.UtcNow,
            Version = "1.0.0"
        };
        
        dbContext.Agents.Add(dbAgent);
        await dbContext.SaveChangesAsync();
        
        // Act
        var (agent, error) = await agentAuth.ValidateAsync(testAgentKey, CancellationToken.None);
        
        // Assert
        Assert.Null(agent);
        Assert.Equal("agent_store_sync_failure", error);
        
        // Verify that UpsertAgentAsync was called 3 times (with retries)
        mockAuthStore.Verify(
            x => x.UpsertAgentAsync(It.IsAny<AgentAuthDto>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3),
            "Sync should be retried 3 times");
        
        // Verify critical error logging
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("CRITICAL") && v.ToString()!.Contains("Failed to sync agent")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "Should log critical error when sync fails");
    }

    [Fact]
    public async Task ValidateAsync_ReturnsFatalError_WhenTableStoreVerificationFailsAfterSync()
    {
        // Arrange
        var mockAuthStore = new Mock<IAgentAuthStore>();
        var mockLogger = new Mock<ILogger<AgentAuth>>();
        var dbContext = CreateInMemoryDbContext();
        
        var agentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var testAgentKey = "test-key-verify-fail";
        
        var agentAuth = new AgentAuth(mockAuthStore.Object, dbContext, mockLogger.Object);
        var hash = agentAuth.HashAgentKey(testAgentKey);
        
        // Setup: First lookup returns null, verification lookup also returns null
        var lookupCount = 0;
        mockAuthStore
            .Setup(x => x.ValidateAgentAsync(hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                lookupCount++;
                return null; // Both initial and verification lookups fail
            });
        
        // Setup: UpsertAgentAsync succeeds
        mockAuthStore
            .Setup(x => x.UpsertAgentAsync(It.IsAny<AgentAuthDto>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        
        // Create agent in database
        var dbAgent = new AgentModel
        {
            Id = agentId,
            TenantId = tenantId,
            Name = "Test Agent",
            AgentKeyHash = hash,
            LastHostname = "test-host",
            LastIp = "192.168.1.1",
            LastSeenUtc = DateTime.UtcNow,
            Version = "1.0.0"
        };
        
        dbContext.Agents.Add(dbAgent);
        await dbContext.SaveChangesAsync();
        
        // Act
        var (agent, error) = await agentAuth.ValidateAsync(testAgentKey, CancellationToken.None);
        
        // Assert
        Assert.Null(agent);
        Assert.Equal("agent_store_sync_failure", error);
        
        // Verify that ValidateAgentAsync was called twice (initial + verification)
        mockAuthStore.Verify(
            x => x.ValidateAgentAsync(hash, It.IsAny<CancellationToken>()),
            Times.Exactly(2),
            "Should lookup twice: initial + verification after sync");
        
        // Verify critical error logging for verification failure
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("CRITICAL") && v.ToString()!.Contains("subsequent lookup FAILED")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "Should log critical error when verification fails");
    }

    [Fact]
    public async Task ValidateAsync_PreventsRepeatedDBHits_WhenAgentInCooldown()
    {
        // Arrange
        var mockAuthStore = new Mock<IAgentAuthStore>();
        var mockLogger = new Mock<ILogger<AgentAuth>>();
        var dbContext = CreateInMemoryDbContext();
        
        var agentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var testAgentKey = "test-key-cooldown";
        
        var agentAuth = new AgentAuth(mockAuthStore.Object, dbContext, mockLogger.Object);
        var hash = agentAuth.HashAgentKey(testAgentKey);
        
        // Setup: Table store returns null
        mockAuthStore
            .Setup(x => x.ValidateAgentAsync(hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentAuthDto?)null);
        
        // Setup: UpsertAgentAsync fails
        mockAuthStore
            .Setup(x => x.UpsertAgentAsync(It.IsAny<AgentAuthDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Table Storage unavailable"));
        
        // Create agent in database
        var dbAgent = new AgentModel
        {
            Id = agentId,
            TenantId = tenantId,
            Name = "Test Agent",
            AgentKeyHash = hash,
            LastHostname = "test-host",
            LastIp = "192.168.1.1",
            LastSeenUtc = DateTime.UtcNow,
            Version = "1.0.0"
        };
        
        dbContext.Agents.Add(dbAgent);
        await dbContext.SaveChangesAsync();
        
        // Act - First attempt: sync fails, agent gets added to cooldown
        var (agent1, error1) = await agentAuth.ValidateAsync(testAgentKey, CancellationToken.None);
        
        // Assert first attempt
        Assert.Null(agent1);
        Assert.Equal("agent_store_sync_failure", error1);
        
        // Act - Second attempt: should fail immediately due to cooldown
        var (agent2, error2) = await agentAuth.ValidateAsync(testAgentKey, CancellationToken.None);
        
        // Assert second attempt
        Assert.Null(agent2);
        Assert.Equal("agent_store_sync_failure", error2);
        
        // Verify UpsertAgentAsync was only called 3 times (from first attempt, not second)
        mockAuthStore.Verify(
            x => x.UpsertAgentAsync(It.IsAny<AgentAuthDto>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3),
            "Sync should only be attempted on first call, not during cooldown");
        
        // Verify cooldown warning was logged
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("cooldown period")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "Should log cooldown warning on second attempt");
    }

    [Fact]
    public async Task ValidateAsync_SucceedsAfterSync_WhenVerificationPasses()
    {
        // Arrange
        var mockAuthStore = new Mock<IAgentAuthStore>();
        var mockLogger = new Mock<ILogger<AgentAuth>>();
        var dbContext = CreateInMemoryDbContext();
        
        var agentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var testAgentKey = "test-key-success";
        
        var agentAuth = new AgentAuth(mockAuthStore.Object, dbContext, mockLogger.Object);
        var hash = agentAuth.HashAgentKey(testAgentKey);
        
        // Setup: First lookup returns null, verification lookup returns the agent
        var lookupCount = 0;
        mockAuthStore
            .Setup(x => x.ValidateAgentAsync(hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                lookupCount++;
                if (lookupCount == 1) return null; // Initial lookup fails
                // Verification lookup succeeds
                return new AgentAuthDto(
                    AgentId: agentId,
                    TenantId: tenantId,
                    Name: "Test Agent",
                    AgentKeyHash: hash,
                    LastHostname: "test-host",
                    LastIp: "192.168.1.1",
                    LastSeenUtc: DateTimeOffset.UtcNow,
                    Version: "1.0.0"
                );
            });
        
        // Setup: UpsertAgentAsync succeeds
        mockAuthStore
            .Setup(x => x.UpsertAgentAsync(It.IsAny<AgentAuthDto>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        
        // Create agent in database
        var dbAgent = new AgentModel
        {
            Id = agentId,
            TenantId = tenantId,
            Name = "Test Agent",
            AgentKeyHash = hash,
            LastHostname = "test-host",
            LastIp = "192.168.1.1",
            LastSeenUtc = DateTime.UtcNow,
            Version = "1.0.0"
        };
        
        dbContext.Agents.Add(dbAgent);
        await dbContext.SaveChangesAsync();
        
        // Act
        var (agent, error) = await agentAuth.ValidateAsync(testAgentKey, CancellationToken.None);
        
        // Assert
        Assert.NotNull(agent);
        Assert.Null(error);
        Assert.Equal(agentId, agent.Id);
        Assert.Equal(tenantId, agent.TenantId);
        
        // Verify that ValidateAgentAsync was called twice (initial + verification)
        mockAuthStore.Verify(
            x => x.ValidateAgentAsync(hash, It.IsAny<CancellationToken>()),
            Times.Exactly(2),
            "Should lookup twice: initial + verification after sync");
        
        // Verify sync was successful
        mockAuthStore.Verify(
            x => x.UpsertAgentAsync(It.IsAny<AgentAuthDto>(), It.IsAny<CancellationToken>()),
            Times.Once);
        
        // Verify verification success log
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Table Store sync verification successful")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ValidateAsync_LogsEnhancedDetails_ForTableStoreLookupFailure()
    {
        // Arrange
        var mockAuthStore = new Mock<IAgentAuthStore>();
        var mockLogger = new Mock<ILogger<AgentAuth>>();
        var dbContext = CreateInMemoryDbContext();
        
        var testAgentKey = "test-key-logging";
        
        // Table store returns null
        mockAuthStore
            .Setup(x => x.ValidateAgentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentAuthDto?)null);
        
        var agentAuth = new AgentAuth(mockAuthStore.Object, dbContext, mockLogger.Object);
        
        // Act
        await agentAuth.ValidateAsync(testAgentKey, CancellationToken.None);
        
        // Assert - Verify enhanced logging
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => 
                    v.ToString()!.Contains("Agent not found in Table Store") &&
                    v.ToString()!.Contains("Falling back to SQL database")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "Should log enhanced details when Table Store lookup fails");
    }

    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        
        var mockTenantProvider = new Mock<ITenantProvider>();
        mockTenantProvider.Setup(x => x.TenantId).Returns(Guid.NewGuid());
        
        return new AppDbContext(options, mockTenantProvider.Object);
    }
}
