using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProdControlAV.Core.Interfaces;
using ProdControlAV.Core.Models;

namespace ProdControlAV.Infrastructure.Services;

/// <summary>
/// Distributed activity monitor using Azure Table Storage for coordination across API and agent instances.
/// Tracks user and agent activity to determine when the system can enter idle mode.
/// </summary>
public class DistributedActivityMonitor : IActivityMonitor
{
    private readonly TableClient _tableClient;
    private readonly ActivityMonitorOptions _options;
    private readonly ILogger<DistributedActivityMonitor> _logger;
    private const string TableName = "SystemActivity";
    private const string PartitionKey = "Activity";

    public DistributedActivityMonitor(
        TableServiceClient tableServiceClient,
        IOptions<ActivityMonitorOptions> options,
        ILogger<DistributedActivityMonitor> logger)
    {
        _options = options.Value;
        _logger = logger;
        _tableClient = tableServiceClient.GetTableClient(TableName);
        
        // Ensure table exists
        try
        {
            _tableClient.CreateIfNotExists();
            _logger.LogInformation("Activity monitor initialized with table: {TableName}", TableName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create activity table {TableName}", TableName);
        }
    }

    public async Task RecordUserActivityAsync(string userId, string tenantId, CancellationToken ct = default)
    {
        try
        {
            var entity = new TableEntity(PartitionKey, $"User-{tenantId}-{userId}")
            {
                { "Type", "User" },
                { "TenantId", tenantId },
                { "UserId", userId },
                { "LastActivityUtc", DateTimeOffset.UtcNow }
            };

            await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
            _logger.LogDebug("Recorded user activity: UserId={UserId}, TenantId={TenantId}", userId, tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record user activity for UserId={UserId}", userId);
        }
    }

    public async Task RecordAgentActivityAsync(string agentId, string tenantId, CancellationToken ct = default)
    {
        try
        {
            var entity = new TableEntity(PartitionKey, $"Agent-{tenantId}-{agentId}")
            {
                { "Type", "Agent" },
                { "TenantId", tenantId },
                { "AgentId", agentId },
                { "LastActivityUtc", DateTimeOffset.UtcNow }
            };

            await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
            _logger.LogDebug("Recorded agent activity: AgentId={AgentId}, TenantId={TenantId}", agentId, tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record agent activity for AgentId={AgentId}", agentId);
        }
    }

    public async Task<bool> IsSystemIdleAsync(CancellationToken ct = default)
    {
        if (!_options.EnableIdleSuspension)
        {
            _logger.LogTrace("Idle suspension is disabled");
            return false;
        }

        try
        {
            var lastActivity = await GetLastActivityAsync(ct);
            if (!lastActivity.HasValue)
            {
                _logger.LogDebug("No activity recorded, system is idle");
                return true;
            }

            var idleThreshold = TimeSpan.FromMinutes(_options.IdleTimeoutMinutes);
            var timeSinceActivity = DateTimeOffset.UtcNow - lastActivity.Value;
            var isIdle = timeSinceActivity >= idleThreshold;

            if (isIdle)
            {
                _logger.LogInformation(
                    "System is idle - last activity was {TimeSinceActivity} ago (threshold: {IdleThreshold})",
                    timeSinceActivity, idleThreshold);
            }
            else
            {
                _logger.LogTrace(
                    "System is active - last activity was {TimeSinceActivity} ago (threshold: {IdleThreshold})",
                    timeSinceActivity, idleThreshold);
            }

            return isIdle;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking idle status, assuming active (fail-safe)");
            return false; // Fail-safe: assume active if we can't determine status
        }
    }

    public async Task<DateTimeOffset?> GetLastActivityAsync(CancellationToken ct = default)
    {
        try
        {
            var cutoffTime = DateTimeOffset.UtcNow.AddMinutes(-_options.IdleTimeoutMinutes * 2);
            
            // Query recent activity entries (last 2x idle timeout to include buffer)
            var filter = $"PartitionKey eq '{PartitionKey}' and Timestamp ge datetime'{cutoffTime:yyyy-MM-ddTHH:mm:ssZ}'";
            var entities = _tableClient.QueryAsync<TableEntity>(filter, cancellationToken: ct);

            DateTimeOffset? lastActivity = null;

            await foreach (var entity in entities)
            {
                if (entity.TryGetValue("LastActivityUtc", out var activityObj) && activityObj is DateTimeOffset activity)
                {
                    if (!lastActivity.HasValue || activity > lastActivity.Value)
                    {
                        lastActivity = activity;
                    }
                }
            }

            return lastActivity;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting last activity");
            return DateTimeOffset.UtcNow; // Fail-safe: assume recent activity
        }
    }

    public async Task<int> GetActiveUserCountAsync(CancellationToken ct = default)
    {
        try
        {
            var cutoffTime = DateTimeOffset.UtcNow.AddMinutes(-_options.IdleTimeoutMinutes);
            
            var filter = $"PartitionKey eq '{PartitionKey}' and Type eq 'User'";
            var entities = _tableClient.QueryAsync<TableEntity>(filter, cancellationToken: ct);

            int count = 0;
            await foreach (var entity in entities)
            {
                if (entity.TryGetValue("LastActivityUtc", out var activityObj) && 
                    activityObj is DateTimeOffset activity &&
                    activity >= cutoffTime)
                {
                    count++;
                }
            }

            _logger.LogDebug("Active user count: {Count}", count);
            return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active user count");
            return 1; // Fail-safe: assume at least one user
        }
    }

    public async Task<int> GetActiveAgentCountAsync(CancellationToken ct = default)
    {
        try
        {
            var cutoffTime = DateTimeOffset.UtcNow.AddMinutes(-_options.IdleTimeoutMinutes);
            
            var filter = $"PartitionKey eq '{PartitionKey}' and Type eq 'Agent'";
            var entities = _tableClient.QueryAsync<TableEntity>(filter, cancellationToken: ct);

            int count = 0;
            await foreach (var entity in entities)
            {
                if (entity.TryGetValue("LastActivityUtc", out var activityObj) && 
                    activityObj is DateTimeOffset activity &&
                    activity >= cutoffTime)
                {
                    count++;
                }
            }

            _logger.LogDebug("Active agent count: {Count}", count);
            return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active agent count");
            return 1; // Fail-safe: assume at least one agent
        }
    }
}
