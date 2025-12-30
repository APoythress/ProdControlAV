using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ProdControlAV.Infrastructure.Services;

/// <summary>
/// Represents an agent authentication record stored in Table Storage
/// </summary>
public record AgentAuthDto(
    Guid AgentId,
    Guid TenantId,
    string Name,
    string AgentKeyHash,
    string? LastHostname = null,
    string? LastIp = null,
    DateTimeOffset? LastSeenUtc = null,
    string? Version = null);

/// <summary>
/// Store for agent authentication data in Azure Table Storage.
/// Eliminates SQL dependency for agent authentication during normal operations.
/// </summary>
public interface IAgentAuthStore
{
    /// <summary>
    /// Validates an agent key hash and returns the agent record if valid
    /// </summary>
    /// <param name="agentKeyHash">SHA256 hash of the agent key</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Agent authentication record if found, null otherwise</returns>
    Task<AgentAuthDto?> ValidateAgentAsync(string agentKeyHash, CancellationToken ct);

    /// <summary>
    /// Updates agent metadata (hostname, IP, version) in Table Storage
    /// </summary>
    /// <param name="agentId">Agent identifier</param>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="hostname">Current hostname</param>
    /// <param name="ipAddress">Current IP address</param>
    /// <param name="version">Agent version</param>
    /// <param name="ct">Cancellation token</param>
    Task UpdateAgentMetadataAsync(Guid agentId, Guid tenantId, string? hostname, string? ipAddress, string? version, CancellationToken ct);

    /// <summary>
    /// Upserts an agent authentication record (typically called during agent creation/update from SQL)
    /// </summary>
    /// <param name="agent">Agent authentication record</param>
    /// <param name="ct">Cancellation token</param>
    Task UpsertAgentAsync(AgentAuthDto agent, CancellationToken ct);

    /// <summary>
    /// Deletes an agent authentication record
    /// </summary>
    /// <param name="agentId">Agent identifier</param>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="ct">Cancellation token</param>
    Task DeleteAgentAsync(Guid agentId, Guid tenantId, CancellationToken ct);

    /// <summary>
    /// Gets all agents for a specific tenant
    /// </summary>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Async enumerable of agent authentication records</returns>
    IAsyncEnumerable<AgentAuthDto> GetAgentsForTenantAsync(Guid tenantId, CancellationToken ct);
}
