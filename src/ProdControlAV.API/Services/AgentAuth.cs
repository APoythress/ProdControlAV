using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ProdControlAV.Infrastructure.Services;

namespace ProdControlAV.API.Services;

public interface IAgentAuth
{
    Task<(Core.Models.Agent? agent, string? error)> ValidateAsync(string agentKey, CancellationToken ct);
    string HashAgentKey(string agentKey);
}

/// <summary>
/// Agent authentication service using Table Storage for lookups.
/// 
/// Authentication Flow:
/// 1. Primary: Authenticate against Table Storage (fast, no SQL dependency)
/// 2. Fallback: If not found in Table Storage, check SQL DB
/// 3. Sync: If found in SQL DB, sync to Table Storage with retry logic
/// 4. Verify: After sync, retry Table Storage lookup to ensure sync succeeded
/// 5. Fail-Fast: If Table Storage lookup fails after successful sync, return fatal error
/// 
/// Failed Agent Tracking:
/// - Agents that fail sync/verification are tracked with timestamp
/// - Prevents repeated SQL DB hits for known-broken agents
/// - Uses 5-minute cooling-off period before retry
/// </summary>
public sealed class AgentAuth : IAgentAuth
{
    private readonly IAgentAuthStore _authStore;
    private readonly AppDbContext _db;
    private readonly ILogger<AgentAuth> _logger;
    
    // Track failed agent keys to prevent repeated DB hits
    private static readonly ConcurrentDictionary<string, DateTimeOffset> _failedAgentKeys = new();
    private static readonly TimeSpan _failedAgentCooldown = TimeSpan.FromMinutes(5);

    public AgentAuth(IAgentAuthStore authStore, AppDbContext db, ILogger<AgentAuth> logger)
    {
        _authStore = authStore;
        _db = db;
        _logger = logger;
    }

    public async Task<(Core.Models.Agent? agent, string? error)> ValidateAsync(string agentKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(agentKey)) return (null, "missing_agent_key");
        
        var hash = HashAgentKey(agentKey);
        _logger.LogDebug("Computed agent key hash for incoming agent: {AgentKeyHash}", hash);
        
        // Always try Table Store lookup first (cheap operation, even during cooldown)
        var agentDto = await _authStore.ValidateAgentAsync(hash, ct);
        
        // If found in table store, clear from failed list (Table Storage may have recovered) and return success
        if (agentDto is not null)
        {
            _failedAgentKeys.TryRemove(hash, out _);
            
            // Convert DTO to Agent model for backward compatibility
            var agent = new Core.Models.Agent
            {
                Id = agentDto.AgentId,
                TenantId = agentDto.TenantId,
                Name = agentDto.Name,
                AgentKeyHash = agentDto.AgentKeyHash,
                LastHostname = agentDto.LastHostname,
                LastIp = agentDto.LastIp,
                LastSeenUtc = agentDto.LastSeenUtc?.UtcDateTime,
                Version = agentDto.Version
            };

            return (agent, null);
        }
        
        // Agent not in Table Store - check if in cooldown before hitting SQL DB
        if (_failedAgentKeys.TryGetValue(hash, out var failedTime))
        {
            if (DateTimeOffset.UtcNow - failedTime < _failedAgentCooldown)
            {
                var remainingCooldown = _failedAgentCooldown - (DateTimeOffset.UtcNow - failedTime);
                _logger.LogWarning("Agent {Hash} is in cooldown period due to previous sync failure. Remaining cooldown: {Cooldown}s", 
                    hash, remainingCooldown.TotalSeconds);
                return (null, "agent_store_sync_failure");
            }
            else
            {
                // Cooldown expired, remove from failed list and allow retry
                _failedAgentKeys.TryRemove(hash, out _);
                _logger.LogInformation("Agent {Hash} cooldown expired, allowing retry", hash);
            }
        }
        
        // Not found in table store and not in cooldown, check database and sync to table store
        _logger.LogInformation("Agent not found in Table Store for hash {Hash}. Reason: Either agent doesn't exist in Table Store or hash index lookup failed. Falling back to SQL database.", hash);
        
        // Query database for agent with matching hash (database is source of truth)
        var dbAgent = await _db.Agents
            .FirstOrDefaultAsync(a => a.AgentKeyHash == hash, ct);
        
        if (dbAgent is null)
        {
            _logger.LogWarning("Agent key hash not found in database: {Hash}. This agent does not exist in the system.", hash);
            return (null, "invalid_agent_key");
        }
        
        _logger.LogInformation("Agent found in SQL database: AgentId={AgentId}, TenantId={TenantId}, Name={Name}. Attempting to sync to Table Store.", 
            dbAgent.Id, dbAgent.TenantId, dbAgent.Name);
        
        // Sync agent from database to table store for future fast lookups with retry logic
        var agentAuthDto = new AgentAuthDto(
            AgentId: dbAgent.Id,
            TenantId: dbAgent.TenantId,
            Name: dbAgent.Name,
            AgentKeyHash: dbAgent.AgentKeyHash,
            LastHostname: dbAgent.LastHostname,
            LastIp: dbAgent.LastIp,
            LastSeenUtc: dbAgent.LastSeenUtc.HasValue ? new DateTimeOffset(dbAgent.LastSeenUtc.Value) : null,
            Version: dbAgent.Version
        );
        
        var syncSuccess = await TrySyncAgentToTableStoreAsync(agentAuthDto, ct);
        if (!syncSuccess)
        {
            _logger.LogError("CRITICAL: Failed to sync agent {AgentId} to Table Storage after all retry attempts. Marking agent as failed to prevent repeated SQL hits.", dbAgent.Id);
            
            // Track this failed agent to prevent repeated DB hits
            _failedAgentKeys[hash] = DateTimeOffset.UtcNow;
            
            return (null, "agent_store_sync_failure");
        }
        
        _logger.LogInformation("Successfully synced agent {AgentId} to Table Store. Verifying sync by retrying Table Store lookup.", dbAgent.Id);
        
        // Verify the sync worked by retrying Table Store lookup
        var verifyDto = await _authStore.ValidateAgentAsync(hash, ct);
        if (verifyDto is null)
        {
            _logger.LogError("CRITICAL: Table Store sync reported success for agent {AgentId}, but subsequent lookup FAILED. Table Store may be inconsistent. Marking agent as failed.", dbAgent.Id);
            
            // Track this failed agent to prevent repeated DB hits
            _failedAgentKeys[hash] = DateTimeOffset.UtcNow;
            
            return (null, "agent_store_sync_failure");
        }
        
        _logger.LogInformation("Table Store sync verification successful for agent {AgentId}. Agent can now authenticate via Table Store.", dbAgent.Id);
        
        // Clear from failed agents list since sync succeeded
        _failedAgentKeys.TryRemove(hash, out _);
        
        // Convert verified DTO to Agent model
        return (new Core.Models.Agent
        {
            Id = verifyDto.AgentId,
            TenantId = verifyDto.TenantId,
            Name = verifyDto.Name,
            AgentKeyHash = verifyDto.AgentKeyHash,
            LastHostname = verifyDto.LastHostname,
            LastIp = verifyDto.LastIp,
            LastSeenUtc = verifyDto.LastSeenUtc?.UtcDateTime,
            Version = verifyDto.Version
        }, null);
    }
    
    /// <summary>
    /// Attempts to sync agent to Table Storage with exponential backoff retry logic.
    /// 
    /// Retry Strategy:
    /// - 3 attempts total
    /// - Exponential backoff: 100ms, 200ms, 400ms
    /// - Logs each attempt and final outcome
    /// 
    /// Returns true if sync succeeded, false if all retries exhausted.
    /// </summary>
    private async Task<bool> TrySyncAgentToTableStoreAsync(AgentAuthDto agentDto, CancellationToken ct)
    {
        const int maxRetries = 3;
        var baseDelay = TimeSpan.FromMilliseconds(100);
        
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                _logger.LogInformation("Attempting to sync agent to Table Store: AgentId={AgentId}, TenantId={TenantId}, Hash={Hash} (attempt {Attempt}/{MaxRetries})", 
                    agentDto.AgentId, agentDto.TenantId, agentDto.AgentKeyHash, attempt, maxRetries);
                
                await _authStore.UpsertAgentAsync(agentDto, ct);
                
                _logger.LogInformation("Successfully synced agent to Table Store: AgentId={AgentId} (attempt {Attempt})", 
                    agentDto.AgentId, attempt);
                return true;
            }
            catch (Exception ex)
            {
                if (attempt == maxRetries)
                {
                    _logger.LogError(ex, "SYNC FAILURE: Failed to sync agent to Table Store after {MaxRetries} attempts: AgentId={AgentId}, TenantId={TenantId}, Hash={Hash}. Exception: {ExceptionType}", 
                        maxRetries, agentDto.AgentId, agentDto.TenantId, agentDto.AgentKeyHash, ex.GetType().Name);
                    return false;
                }
                
                var delay = baseDelay * Math.Pow(2, attempt - 1); // Exponential backoff
                _logger.LogWarning(ex, "Sync attempt {Attempt}/{MaxRetries} failed for agent {AgentId}. Error: {ErrorMessage}. Retrying in {Delay}ms", 
                    attempt, maxRetries, agentDto.AgentId, ex.Message, delay.TotalMilliseconds);
                
                await Task.Delay(delay, ct);
            }
        }
        
        return false;
    }

    public string HashAgentKey(string agentKey)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(agentKey)));
    }
}