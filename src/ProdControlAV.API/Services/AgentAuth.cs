using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProdControlAV.Core.Models;
using ProdControlAV.Infrastructure.Services;

namespace ProdControlAV.API.Services;

public interface IAgentAuth
{
    Task<(Agent? agent, string? error)> ValidateAsync(string agentKey, CancellationToken ct);
    string HashAgentKey(string agentKey);
}

/// <summary>
/// Agent authentication service using Table Storage for lookups.
/// Falls back to SQL database when agent not found in table store to enable initial sync.
/// </summary>
public sealed class AgentAuth : IAgentAuth
{
    private readonly IAgentAuthStore _authStore;
    private readonly AppDbContext _db;
    private readonly ILogger<AgentAuth> _logger;

    public AgentAuth(IAgentAuthStore authStore, AppDbContext db, ILogger<AgentAuth> logger)
    {
        _authStore = authStore;
        _db = db;
        _logger = logger;
    }

    public async Task<(Agent? agent, string? error)> ValidateAsync(string agentKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(agentKey)) return (null, "missing_agent_key");
        
        var hash = HashAgentKey(agentKey);
        _logger.LogDebug("Computed agent key hash for incoming agent: {AgentKeyHash}", hash);
        var agentDto = await _authStore.ValidateAgentAsync(hash, ct);
        
        // If not found in table store, check database and sync to table store
        if (agentDto is null)
        {
            _logger.LogInformation("Agent not found in table store for hash {Hash}, checking database", hash);
            
            // Query database for agent with matching hash (database is source of truth)
            var dbAgent = await _db.Agents
                .FirstOrDefaultAsync(a => a.AgentKeyHash == hash, ct);
            
            if (dbAgent is null)
            {
                _logger.LogWarning("Agent key hash not found in database: {Hash}", hash);
                return (null, "invalid_agent_key");
            }
            
            _logger.LogInformation("Agent found in database: AgentId={AgentId}, TenantId={TenantId}, syncing to table store", 
                dbAgent.Id, dbAgent.TenantId);
            
            // Sync agent from database to table store for future fast lookups
            try
            {
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
                
                await _authStore.UpsertAgentAsync(agentAuthDto, ct);
                _logger.LogInformation("Successfully synced agent to table store: AgentId={AgentId}", dbAgent.Id);
            }
            catch (Exception ex)
            {
                // Log error but don't fail authentication - table store sync is for performance, not correctness
                _logger.LogError(ex, "Failed to sync agent to table store: AgentId={AgentId}", dbAgent.Id);
            }
            
            // Return the database agent
            return (dbAgent, null);
        }

        // Convert DTO to Agent model for backward compatibility
        var agent = new Agent
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

    public string HashAgentKey(string agentKey)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(agentKey)));
    }
}