using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProdControlAV.Infrastructure.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ProdControlAV.API.Controllers;

/// <summary>
/// Health check endpoints for monitoring system status and Table Storage connectivity
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAgentAuthStore _agentAuthStore;
    private readonly IDeviceStore _deviceStore;
    private readonly IDeviceStatusStore _statusStore;
    private readonly ILogger<HealthController> _logger;

    public HealthController(
        AppDbContext db,
        IAgentAuthStore agentAuthStore,
        IDeviceStore deviceStore,
        IDeviceStatusStore statusStore,
        ILogger<HealthController> logger)
    {
        _db = db;
        _agentAuthStore = agentAuthStore;
        _deviceStore = deviceStore;
        _statusStore = statusStore;
        _logger = logger;
    }

    /// <summary>
    /// Basic health check - returns 200 OK if API is running
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Get()
    {
        return Ok(new
        {
            status = "healthy",
            timestamp = DateTime.UtcNow,
            version = "1.0"
        });
    }

    /// <summary>
    /// Check Table Storage connectivity and performance
    /// </summary>
    [HttpGet("storage")]
    [AllowAnonymous]
    public async Task<IActionResult> CheckTableStorage(CancellationToken ct)
    {
        var agentAuthCheck = await CheckAgentAuthStoreAsync(ct);
        var deviceCheck = await CheckDeviceStoreAsync(ct);
        var statusCheck = await CheckDeviceStatusStoreAsync(ct);

        var checks = new
        {
            agentAuthStore = agentAuthCheck,
            deviceStore = deviceCheck,
            statusStore = statusCheck
        };

        var allHealthy = agentAuthCheck.healthy &&
                        deviceCheck.healthy &&
                        statusCheck.healthy;

        return allHealthy
            ? Ok(new { status = "healthy", timestamp = DateTime.UtcNow, checks })
            : StatusCode(503, new { status = "unhealthy", timestamp = DateTime.UtcNow, checks });
    }

    /// <summary>
    /// Check agent sync status - identifies agents that need Table Storage sync
    /// </summary>
    [HttpGet("agent-sync")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> CheckAgentSyncStatus(CancellationToken ct)
    {
        try
        {
            // Get count of agents in SQL
            var sqlAgentCount = await _db.Agents.CountAsync(ct);

            // Sample check: Try to validate a known agent from SQL
            var sampleAgent = await _db.Agents.FirstOrDefaultAsync(ct);
            
            var sampleSyncStatus = "no_agents_in_sql";
            if (sampleAgent != null)
            {
                // Check if this agent is in Table Storage
                var agentDto = await _agentAuthStore.ValidateAgentAsync(sampleAgent.AgentKeyHash, ct);
                sampleSyncStatus = agentDto != null ? "synced" : "not_synced";
            }

            return Ok(new
            {
                status = "healthy",
                timestamp = DateTime.UtcNow,
                sqlAgentCount,
                sampleAgentSyncStatus = sampleSyncStatus,
                recommendation = sampleSyncStatus == "not_synced"
                    ? "Some agents may not be synced to Table Storage. Restart agents to trigger sync."
                    : "Agent sync appears healthy"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking agent sync status");
            return StatusCode(500, new
            {
                status = "error",
                message = ex.Message,
                timestamp = DateTime.UtcNow
            });
        }
    }

    /// <summary>
    /// Check database connectivity
    /// </summary>
    [HttpGet("database")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> CheckDatabase(CancellationToken ct)
    {
        try
        {
            var startTime = DateTime.UtcNow;
            
            // Simple query to test connectivity
            var canConnect = await _db.Database.CanConnectAsync(ct);
            
            var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;

            return Ok(new
            {
                status = canConnect ? "healthy" : "unhealthy",
                timestamp = DateTime.UtcNow,
                responseTimeMs = elapsed,
                connected = canConnect
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking database connectivity");
            return StatusCode(503, new
            {
                status = "unhealthy",
                message = ex.Message,
                timestamp = DateTime.UtcNow
            });
        }
    }

    private async Task<(bool healthy, double? responseTimeMs, string message)> CheckAgentAuthStoreAsync(CancellationToken ct)
    {
        try
        {
            var startTime = DateTime.UtcNow;
            
            // Try to validate with a non-existent hash (should return null quickly)
            var testHash = "0000TEST0000HASH0000TEST0000HASH";
            var result = await _agentAuthStore.ValidateAgentAsync(testHash, ct);
            
            var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;

            return (true, elapsed, "AgentAuthStore responding");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AgentAuthStore health check failed");
            return (false, null, ex.Message);
        }
    }

    private async Task<(bool healthy, double? responseTimeMs, string message)> CheckDeviceStoreAsync(CancellationToken ct)
    {
        try
        {
            var startTime = DateTime.UtcNow;
            
            // Try to enumerate devices for a test tenant (will be empty but tests connectivity)
            var testTenantId = Guid.Empty;
            var count = 0;
            await foreach (var device in _deviceStore.GetAllForTenantAsync(testTenantId, ct))
            {
                count++;
                break; // Just test that we can start enumeration
            }
            
            var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;

            return (true, elapsed, "DeviceStore responding");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DeviceStore health check failed");
            return (false, null, ex.Message);
        }
    }

    private async Task<(bool healthy, double? responseTimeMs, string message)> CheckDeviceStatusStoreAsync(CancellationToken ct)
    {
        try
        {
            var startTime = DateTime.UtcNow;
            
            // Try to get status for a test device (should return null quickly)
            var testTenantId = Guid.Empty;
            var testDeviceId = Guid.Empty;
            var result = await _statusStore.GetDeviceStatusAsync(testTenantId, testDeviceId, ct);
            
            var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;

            return (true, elapsed, "DeviceStatusStore responding");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DeviceStatusStore health check failed");
            return (false, null, ex.Message);
        }
    }
}
