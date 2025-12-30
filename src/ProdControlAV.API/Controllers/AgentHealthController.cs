using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ProdControlAV.API.Models;
using ProdControlAV.Infrastructure.Services;

namespace ProdControlAV.API.Controllers;

/// <summary>
/// Controller for agent health monitoring and dashboard
/// </summary>
[ApiController]
[Route("api/agent")]
[Authorize(Policy = "DevAdmin")]
public sealed class AgentHealthController : ControllerBase
{
    private readonly IAgentAuthStore _agentAuthStore;
    private readonly ICommandQueueStore _commandQueueStore;
    private readonly ICommandHistoryStore _commandHistoryStore;
    private readonly ILogger<AgentHealthController> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    
    // Agent is considered online if last seen within this threshold
    private static readonly TimeSpan OnlineThreshold = TimeSpan.FromSeconds(90);
    
    // Query window for command history (48 hours)
    private static readonly int HistoryWindowDays = 2;

    public AgentHealthController(
        IAgentAuthStore agentAuthStore,
        ICommandQueueStore commandQueueStore,
        ICommandHistoryStore commandHistoryStore,
        ILogger<AgentHealthController> logger,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _agentAuthStore = agentAuthStore;
        _commandQueueStore = commandQueueStore;
        _commandHistoryStore = commandHistoryStore;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    /// <summary>
    /// Get health dashboard data for all agents in the current tenant
    /// Accessible only by DevAdmin users
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Agent health dashboard data</returns>
    [HttpGet("health-dashboard")]
    [ProducesResponseType<AgentHealthDashboardResponse>(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<ActionResult<AgentHealthDashboardResponse>> GetHealthDashboard(CancellationToken ct)
    {
        // Get tenant ID from authenticated user's claims
        var tenantIdClaim = User.FindFirstValue("tenant_id");
        if (!Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            _logger.LogWarning("[HEALTH-DASHBOARD] Invalid or missing tenant_id claim");
            return Unauthorized(new { error = "invalid_tenant" });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        _logger.LogInformation("[HEALTH-DASHBOARD] User {UserId} accessing health dashboard for tenant {TenantId}", 
            userId, tenantId);

        try
        {
            var response = new AgentHealthDashboardResponse();
            
            // Get latest version from appcast if configured
            var latestVersion = await GetLatestVersionFromAppcastAsync(ct);

            // Query tenant-wide command stats once (more efficient than per-agent)
            // Note: CommandQueue and CommandHistory don't track AgentId, only DeviceId and TenantId
            var pendingCommands = new List<CommandQueueDto>();
            await foreach (var cmd in _commandQueueStore.GetPendingForTenantAsync(tenantId, ct))
            {
                pendingCommands.Add(cmd);
            }
            var tenantPendingCount = pendingCommands.Count;

            // Get command history for last 48 hours (tenant-wide)
            var historyEntries = new List<CommandHistoryDto>();
            await foreach (var history in _commandHistoryStore.GetRecentHistoryForTenantAsync(tenantId, HistoryWindowDays, ct))
            {
                historyEntries.Add(history);
            }

            // Calculate tenant-wide success/failure counts
            var tenantSuccessCount = historyEntries.Count(h => h.Success);
            var tenantFailureCount = historyEntries.Count(h => !h.Success);

            // Get tenant-wide recent errors (last 48h, max 5, most recent first)
            var tenantRecentErrors = historyEntries
                .Where(h => !h.Success && !string.IsNullOrEmpty(h.ErrorMessage))
                .OrderByDescending(h => h.ExecutedUtc)
                .Take(5)
                .Select(h => new AgentErrorInfo
                {
                    Timestamp = h.ExecutedUtc,
                    Message = h.ErrorMessage ?? "Unknown error"
                })
                .ToList();

            // Query all agents for the tenant from Table Storage
            await foreach (var agent in _agentAuthStore.GetAgentsForTenantAsync(tenantId, ct))
            {
                var healthInfo = new AgentHealthInfo
                {
                    AgentId = agent.AgentId.ToString(),
                    Name = agent.Name ?? agent.LastHostname ?? "Unknown Agent",
                    Version = agent.Version,
                    LastSeenUtc = agent.LastSeenUtc
                };

                // Determine online/offline status based on last seen time
                if (agent.LastSeenUtc.HasValue)
                {
                    var timeSinceLastSeen = DateTimeOffset.UtcNow - agent.LastSeenUtc.Value;
                    healthInfo.Status = timeSinceLastSeen <= OnlineThreshold ? "online" : "offline";
                }
                else
                {
                    healthInfo.Status = "offline";
                }

                // Check if agent is up to date
                if (!string.IsNullOrEmpty(latestVersion) && !string.IsNullOrEmpty(agent.Version))
                {
                    healthInfo.IsUpToDate = IsVersionUpToDate(agent.Version, latestVersion);
                    if (!healthInfo.IsUpToDate)
                    {
                        healthInfo.VersionAvailable = latestVersion;
                    }
                }
                else
                {
                    // If we can't determine version, assume up to date
                    healthInfo.IsUpToDate = true;
                }

                // Assign tenant-wide command stats to this agent
                // Note: These stats are organization-wide since the command system doesn't track per-agent execution
                healthInfo.CommandsPending = tenantPendingCount;
                healthInfo.CommandsPolledSuccessful = tenantSuccessCount;
                healthInfo.CommandsPolledUnsuccessful = tenantFailureCount;
                healthInfo.RecentErrors = tenantRecentErrors;

                response.Agents.Add(healthInfo);
            }

            _logger.LogInformation("[HEALTH-DASHBOARD] Returning health data for {Count} agents in tenant {TenantId}", 
                response.Agents.Count, tenantId);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[HEALTH-DASHBOARD] Error retrieving health dashboard for tenant {TenantId}", tenantId);
            return StatusCode(500, new { error = "failed_to_retrieve_health_data" });
        }
    }

    /// <summary>
    /// Fetches the latest version from the appcast.json manifest
    /// </summary>
    private async Task<string?> GetLatestVersionFromAppcastAsync(CancellationToken ct)
    {
        try
        {
            var appcastUrl = _configuration["Update:AppcastUrl"];
            if (string.IsNullOrEmpty(appcastUrl))
            {
                _logger.LogDebug("[HEALTH-DASHBOARD] Appcast URL not configured, skipping version check");
                return null;
            }

            var httpClient = _httpClientFactory.CreateClient();
            var appcast = await httpClient.GetFromJsonAsync<AppcastManifest>(appcastUrl, ct);
            
            if (appcast?.Items == null || appcast.Items.Count == 0)
            {
                _logger.LogWarning("[HEALTH-DASHBOARD] No items found in appcast manifest");
                return null;
            }

            // Get the latest version (first item in the appcast)
            var latestItem = appcast.Items
                .OrderByDescending(i => ParseVersion(i.Version))
                .FirstOrDefault();

            _logger.LogDebug("[HEALTH-DASHBOARD] Latest version from appcast: {Version}", latestItem?.Version);
            return latestItem?.Version;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[HEALTH-DASHBOARD] Failed to fetch appcast manifest");
            return null;
        }
    }

    /// <summary>
    /// Compares two version strings
    /// </summary>
    private bool IsVersionUpToDate(string currentVersion, string latestVersion)
    {
        try
        {
            var current = ParseVersion(currentVersion);
            var latest = ParseVersion(latestVersion);
            return current >= latest;
        }
        catch
        {
            // If we can't parse versions, assume up to date
            return true;
        }
    }

    /// <summary>
    /// Parses a version string into a comparable Version object
    /// </summary>
    private Version ParseVersion(string? versionString)
    {
        if (string.IsNullOrEmpty(versionString))
            return new Version(0, 0, 0);

        // Remove 'v' prefix if present
        versionString = versionString.TrimStart('v', 'V');
        
        if (Version.TryParse(versionString, out var version))
            return version;

        return new Version(0, 0, 0);
    }

    // DTOs for appcast.json parsing
    private sealed class AppcastManifest
    {
        public List<AppcastItem> Items { get; set; } = new();
    }

    private sealed class AppcastItem
    {
        public string Version { get; set; } = string.Empty;
        public string ShortVersion { get; set; } = string.Empty;
    }
}
