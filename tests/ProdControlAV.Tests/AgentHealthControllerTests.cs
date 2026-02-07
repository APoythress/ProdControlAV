using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;
using ProdControlAV.API.Controllers;
using ProdControlAV.API.Models;
using ProdControlAV.Infrastructure.Services;

namespace ProdControlAV.Tests;

public class AgentHealthControllerTests
{
    private static async IAsyncEnumerable<T> GetAsyncEnumerable<T>(List<T> items)
    {
        foreach (var item in items)
        {
            yield return item;
            await Task.CompletedTask;
        }
    }

    [Fact]
    public async Task GetHealthDashboard_ReturnsUnauthorized_WhenTenantIdMissing()
    {
        // Arrange
        var agentAuthStoreMock = new Mock<IAgentAuthStore>();
        var commandQueueStoreMock = new Mock<ICommandQueueStore>();
        var commandHistoryStoreMock = new Mock<ICommandHistoryStore>();
        var loggerMock = new Mock<ILogger<AgentHealthController>>();
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        var configurationMock = new Mock<IConfiguration>();

        var controller = new AgentHealthController(
            agentAuthStoreMock.Object,
            commandQueueStoreMock.Object,
            commandHistoryStoreMock.Object,
            loggerMock.Object,
            httpClientFactoryMock.Object,
            configurationMock.Object
        );

        // Setup user with no tenant_id claim
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) };
        var identity = new ClaimsIdentity(claims, "mock");
        var user = new ClaimsPrincipal(identity);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        // Act
        var result = await controller.GetHealthDashboard(CancellationToken.None);

        // Assert
        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        Assert.NotNull(unauthorizedResult.Value);
    }

    [Fact]
    public async Task GetHealthDashboard_ReturnsTenantScopedAgents_WhenValidTenant()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var agent1Id = Guid.NewGuid();
        var agent2Id = Guid.NewGuid();

        var agentAuthStoreMock = new Mock<IAgentAuthStore>();
        var commandQueueStoreMock = new Mock<ICommandQueueStore>();
        var commandHistoryStoreMock = new Mock<ICommandHistoryStore>();
        var loggerMock = new Mock<ILogger<AgentHealthController>>();
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        var configurationMock = new Mock<IConfiguration>();

        // Setup agents for tenant
        var agents = new List<AgentAuthDto>
        {
            new AgentAuthDto(
                AgentId: agent1Id,
                TenantId: tenantId,
                Name: "Agent 1",
                AgentKeyHash: "hash1",
                LastHostname: "host1",
                LastIp: "192.168.1.1",
                LastSeenUtc: DateTimeOffset.UtcNow.AddSeconds(-30), // Online
                Version: "0.3.0"
            ),
            new AgentAuthDto(
                AgentId: agent2Id,
                TenantId: tenantId,
                Name: "Agent 2",
                AgentKeyHash: "hash2",
                LastHostname: "host2",
                LastIp: "192.168.1.2",
                LastSeenUtc: DateTimeOffset.UtcNow.AddMinutes(-5), // Offline
                Version: "0.2.0"
            )
        };

        agentAuthStoreMock
            .Setup(s => s.GetAgentsForTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .Returns(GetAsyncEnumerable(agents));

        // Setup empty command queue
        commandQueueStoreMock
            .Setup(s => s.GetPendingForTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .Returns(GetAsyncEnumerable(new List<CommandQueueDto>()));

        // Setup empty command history
        commandHistoryStoreMock
            .Setup(s => s.GetRecentHistoryForTenantAsync(tenantId, 2, It.IsAny<CancellationToken>()))
            .Returns(GetAsyncEnumerable(new List<CommandHistoryDto>()));

        var controller = new AgentHealthController(
            agentAuthStoreMock.Object,
            commandQueueStoreMock.Object,
            commandHistoryStoreMock.Object,
            loggerMock.Object,
            httpClientFactoryMock.Object,
            configurationMock.Object
        );

        // Setup user with tenant_id claim
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim("tenant_id", tenantId.ToString())
        };
        var identity = new ClaimsIdentity(claims, "mock");
        var user = new ClaimsPrincipal(identity);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        // Act
        var result = await controller.GetHealthDashboard(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<AgentHealthDashboardResponse>(okResult.Value);
        Assert.Equal(2, response.Agents.Count);

        var agent1 = response.Agents.First(a => a.AgentId == agent1Id.ToString());
        Assert.Equal("online", agent1.Status);
        Assert.Equal("Agent 1", agent1.Name);
        Assert.Equal("0.3.0", agent1.Version);

        var agent2 = response.Agents.First(a => a.AgentId == agent2Id.ToString());
        Assert.Equal("offline", agent2.Status);
        Assert.Equal("Agent 2", agent2.Name);
        Assert.Equal("0.2.0", agent2.Version);
    }

    [Fact]
    public async Task GetHealthDashboard_CalculatesOnlineOfflineStatus_BasedOnLastSeen()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var onlineAgentId = Guid.NewGuid();
        var offlineAgentId = Guid.NewGuid();
        var neverSeenAgentId = Guid.NewGuid();

        var agentAuthStoreMock = new Mock<IAgentAuthStore>();
        var commandQueueStoreMock = new Mock<ICommandQueueStore>();
        var commandHistoryStoreMock = new Mock<ICommandHistoryStore>();
        var loggerMock = new Mock<ILogger<AgentHealthController>>();
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        var configurationMock = new Mock<IConfiguration>();

        var agents = new List<AgentAuthDto>
        {
            new AgentAuthDto(onlineAgentId, tenantId, "Online Agent", "hash1", 
                LastHostname: "host1", LastIp: "192.168.1.1",
                LastSeenUtc: DateTimeOffset.UtcNow.AddSeconds(-60), Version: "0.3.0"), // Within 90s threshold
            new AgentAuthDto(offlineAgentId, tenantId, "Offline Agent", "hash2",
                LastHostname: "host2", LastIp: "192.168.1.2", 
                LastSeenUtc: DateTimeOffset.UtcNow.AddSeconds(-120), Version: "0.3.0"), // Beyond 90s threshold
            new AgentAuthDto(neverSeenAgentId, tenantId, "Never Seen Agent", "hash3",
                LastHostname: "host3", LastIp: "192.168.1.3", 
                LastSeenUtc: null, Version: "0.3.0") // Never seen
        };

        agentAuthStoreMock
            .Setup(s => s.GetAgentsForTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .Returns(GetAsyncEnumerable(agents));

        commandQueueStoreMock
            .Setup(s => s.GetPendingForTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .Returns(GetAsyncEnumerable(new List<CommandQueueDto>()));

        commandHistoryStoreMock
            .Setup(s => s.GetRecentHistoryForTenantAsync(tenantId, 2, It.IsAny<CancellationToken>()))
            .Returns(GetAsyncEnumerable(new List<CommandHistoryDto>()));

        var controller = new AgentHealthController(
            agentAuthStoreMock.Object,
            commandQueueStoreMock.Object,
            commandHistoryStoreMock.Object,
            loggerMock.Object,
            httpClientFactoryMock.Object,
            configurationMock.Object
        );

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim("tenant_id", tenantId.ToString())
        };
        var identity = new ClaimsIdentity(claims, "mock");
        var user = new ClaimsPrincipal(identity);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        // Act
        var result = await controller.GetHealthDashboard(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<AgentHealthDashboardResponse>(okResult.Value);
        
        var onlineAgent = response.Agents.First(a => a.AgentId == onlineAgentId.ToString());
        Assert.Equal("online", onlineAgent.Status);

        var offlineAgent = response.Agents.First(a => a.AgentId == offlineAgentId.ToString());
        Assert.Equal("offline", offlineAgent.Status);

        var neverSeenAgent = response.Agents.First(a => a.AgentId == neverSeenAgentId.ToString());
        Assert.Equal("offline", neverSeenAgent.Status);
    }

    [Fact]
    public async Task GetHealthDashboard_AggregatesCommandStats_ForLast48Hours()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();

        var agentAuthStoreMock = new Mock<IAgentAuthStore>();
        var commandQueueStoreMock = new Mock<ICommandQueueStore>();
        var commandHistoryStoreMock = new Mock<ICommandHistoryStore>();
        var loggerMock = new Mock<ILogger<AgentHealthController>>();
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        var configurationMock = new Mock<IConfiguration>();

        var agents = new List<AgentAuthDto>
        {
            new AgentAuthDto(agentId, tenantId, "Test Agent", "hash",
                LastHostname: "host1", LastIp: "192.168.1.1",
                LastSeenUtc: DateTimeOffset.UtcNow, Version: "0.3.0")
        };

        agentAuthStoreMock
            .Setup(s => s.GetAgentsForTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .Returns(GetAsyncEnumerable(agents));

        // Setup pending commands (2 pending)
        var pendingCommands = new List<CommandQueueDto>
        {
            new CommandQueueDto(
                Guid.NewGuid(), 
                tenantId, 
                deviceId, 
                "Command1", 
                "REST", 
                null, 
                "GET", 
                null, 
                null, 
                DateTimeOffset.UtcNow, 
                Guid.NewGuid(),
                null,
                null,
                null,
                false,
                null,
                null,
                null,
                null,
                null,
                60,
                "Pending",
                0
            ),
            new CommandQueueDto(
                Guid.NewGuid(), 
                tenantId, 
                deviceId, 
                "Command2", 
                "REST", 
                null, 
                "GET", 
                null, 
                null, 
                DateTimeOffset.UtcNow, 
                Guid.NewGuid(),
                null,
                null,
                null,
                false,
                null,
                null,
                null,
                null,
                null,
                60,
                "Pending",
                0
            ),
        };

        commandQueueStoreMock
            .Setup(s => s.GetPendingForTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .Returns(GetAsyncEnumerable(pendingCommands));

        // Setup command history (3 successful, 1 failed)
        var historyEntries = new List<CommandHistoryDto>
        {
            new CommandHistoryDto(Guid.NewGuid(), Guid.NewGuid(), tenantId, deviceId, "Cmd1", 
                DateTimeOffset.UtcNow.AddHours(-1), true),
            new CommandHistoryDto(Guid.NewGuid(), Guid.NewGuid(), tenantId, deviceId, "Cmd2", 
                DateTimeOffset.UtcNow.AddHours(-2), true),
            new CommandHistoryDto(Guid.NewGuid(), Guid.NewGuid(), tenantId, deviceId, "Cmd3", 
                DateTimeOffset.UtcNow.AddHours(-3), true),
            new CommandHistoryDto(Guid.NewGuid(), Guid.NewGuid(), tenantId, deviceId, "Cmd4", 
                DateTimeOffset.UtcNow.AddHours(-4), false, "Connection timeout")
        };

        commandHistoryStoreMock
            .Setup(s => s.GetRecentHistoryForTenantAsync(tenantId, 2, It.IsAny<CancellationToken>()))
            .Returns(GetAsyncEnumerable(historyEntries));

        var controller = new AgentHealthController(
            agentAuthStoreMock.Object,
            commandQueueStoreMock.Object,
            commandHistoryStoreMock.Object,
            loggerMock.Object,
            httpClientFactoryMock.Object,
            configurationMock.Object
        );

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim("tenant_id", tenantId.ToString())
        };
        var identity = new ClaimsIdentity(claims, "mock");
        var user = new ClaimsPrincipal(identity);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        // Act
        var result = await controller.GetHealthDashboard(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<AgentHealthDashboardResponse>(okResult.Value);
        
        var agent = response.Agents.First();
        Assert.Equal(2, agent.CommandsPending);
        Assert.Equal(3, agent.CommandsPolledSuccessful);
        Assert.Equal(1, agent.CommandsPolledUnsuccessful);
        Assert.Single(agent.RecentErrors);
        Assert.Equal("Connection timeout", agent.RecentErrors[0].Message);
    }

    [Fact]
    public async Task GetHealthDashboard_LimitsRecentErrors_ToMaxOf5()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();

        var agentAuthStoreMock = new Mock<IAgentAuthStore>();
        var commandQueueStoreMock = new Mock<ICommandQueueStore>();
        var commandHistoryStoreMock = new Mock<ICommandHistoryStore>();
        var loggerMock = new Mock<ILogger<AgentHealthController>>();
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        var configurationMock = new Mock<IConfiguration>();

        var agents = new List<AgentAuthDto>
        {
            new AgentAuthDto(agentId, tenantId, "Test Agent", "hash",
                LastHostname: "host1", LastIp: "192.168.1.1",
                LastSeenUtc: DateTimeOffset.UtcNow, Version: "0.3.0")
        };

        agentAuthStoreMock
            .Setup(s => s.GetAgentsForTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .Returns(GetAsyncEnumerable(agents));

        commandQueueStoreMock
            .Setup(s => s.GetPendingForTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .Returns(GetAsyncEnumerable(new List<CommandQueueDto>()));

        // Setup 8 failed commands
        var historyEntries = new List<CommandHistoryDto>();
        for (int i = 1; i <= 8; i++)
        {
            historyEntries.Add(new CommandHistoryDto(
                Guid.NewGuid(), Guid.NewGuid(), tenantId, deviceId, $"Cmd{i}",
                DateTimeOffset.UtcNow.AddHours(-i), false, $"Error {i}"));
        }

        commandHistoryStoreMock
            .Setup(s => s.GetRecentHistoryForTenantAsync(tenantId, 2, It.IsAny<CancellationToken>()))
            .Returns(GetAsyncEnumerable(historyEntries));

        var controller = new AgentHealthController(
            agentAuthStoreMock.Object,
            commandQueueStoreMock.Object,
            commandHistoryStoreMock.Object,
            loggerMock.Object,
            httpClientFactoryMock.Object,
            configurationMock.Object
        );

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim("tenant_id", tenantId.ToString())
        };
        var identity = new ClaimsIdentity(claims, "mock");
        var user = new ClaimsPrincipal(identity);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        // Act
        var result = await controller.GetHealthDashboard(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<AgentHealthDashboardResponse>(okResult.Value);
        
        var agent = response.Agents.First();
        Assert.Equal(5, agent.RecentErrors.Count); // Limited to 5
        Assert.Equal("Error 1", agent.RecentErrors[0].Message); // Most recent first
    }
}
