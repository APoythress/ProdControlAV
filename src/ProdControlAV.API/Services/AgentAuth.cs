using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
/// Eliminates SQL dependency for agent authentication during normal operations.
/// </summary>
public sealed class AgentAuth : IAgentAuth
{
    private readonly IAgentAuthStore _authStore;
    private readonly ILogger<AgentAuth> _logger;

    public AgentAuth(IAgentAuthStore authStore, ILogger<AgentAuth> logger)
    {
        _authStore = authStore;
        _logger = logger;
    }

    public async Task<(Agent? agent, string? error)> ValidateAsync(string agentKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(agentKey)) return (null, "missing_agent_key");
        
        var hash = HashAgentKey(agentKey);
        _logger.LogDebug("Computed agent key hash for incoming agent: {AgentKeyHash}", hash);
        var agentDto = await _authStore.ValidateAgentAsync(hash, ct);
        
        if (agentDto is null) return (null, "invalid_agent_key");

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