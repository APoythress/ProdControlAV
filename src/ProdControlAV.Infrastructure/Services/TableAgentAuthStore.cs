using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;

namespace ProdControlAV.Infrastructure.Services;

/// <summary>
/// Azure Table Storage implementation of agent authentication store.
/// Uses an indexed secondary table for efficient key hash lookups.
/// </summary>
public sealed class TableAgentAuthStore : IAgentAuthStore
{
    private readonly TableClient _agentsTable;
    private readonly TableClient _agentKeyHashIndex;
    private readonly ILogger<TableAgentAuthStore> _logger;

    public TableAgentAuthStore(
        TableServiceClient tableServiceClient,
        ILogger<TableAgentAuthStore> logger)
    {
        _logger = logger;
        _agentsTable = tableServiceClient.GetTableClient("Agents");
        _agentKeyHashIndex = tableServiceClient.GetTableClient("AgentKeyHashIndex");

        // Ensure tables exist
        try
        {
            _agentsTable.CreateIfNotExists();
            _agentKeyHashIndex.CreateIfNotExists();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create agent auth tables");
        }
    }

    public async Task<AgentAuthDto?> ValidateAgentAsync(string agentKeyHash, CancellationToken ct)
    {
        try
        {
            // Lookup agent ID from hash index table
            // PartitionKey = first 4 chars of hash for distribution, RowKey = full hash
            // Normalize hash to a consistent casing to avoid duplicate index rows (DB may store different case)
            var normalizedHash = (agentKeyHash ?? string.Empty).ToUpperInvariant();
            var partitionKey = normalizedHash.Length >= 4 ? normalizedHash[..4] : normalizedHash;
            
            TableEntity? indexEntity;
            try
            {
                indexEntity = await _agentKeyHashIndex.GetEntityAsync<TableEntity>(partitionKey, normalizedHash, cancellationToken: ct);
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
            {
                _logger.LogDebug("Agent key hash not found in index: {Hash}", agentKeyHash);
                return null;
            }

            if (!indexEntity.TryGetValue("AgentId", out var agentIdObj) ||
                !indexEntity.TryGetValue("TenantId", out var tenantIdObj))
            {
                var presentKeys = indexEntity.Keys != null ? string.Join(", ", indexEntity.Keys) : "(none)";
                _logger.LogDebug("Index entity for hash {Hash} missing required properties; present properties: {PresentKeys}", 
                    normalizedHash, presentKeys);
                _logger.LogWarning("Invalid index entry for hash: {Hash}", agentKeyHash);
                return null;
            }

            var agentId = Guid.Parse(agentIdObj.ToString()!);
            var tenantId = Guid.Parse(tenantIdObj.ToString()!);

            // Fetch full agent record
            TableEntity? agentEntity;
            try
            {
                var agentPartitionKey = tenantId.ToString().ToLowerInvariant();
                var agentRowKey = agentId.ToString();
                
                // Purpose: log exactly what keys/filters you are using so you can confirm they're correct.
                _logger.LogDebug("[TableAgentAuthStore] Looking up agent. Table={Table}, PartitionKey={PartitionKey}, RowKey={RowKey}, Filter={Filter}",
                    _agentsTable.Name, agentPartitionKey, agentRowKey, "(none - direct lookup)");
                
                agentEntity = await _agentsTable.GetEntityAsync<TableEntity>(
                    agentPartitionKey,
                    agentRowKey,
                    cancellationToken: ct);
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
            {
                _logger.LogWarning("Agent not found in main table: AgentId={AgentId}, TenantId={TenantId}", agentId, tenantId);
                return null;
            }

            return new AgentAuthDto(
                AgentId: agentId,
                TenantId: tenantId,
                Name: agentEntity.GetString("Name") ?? "Agent",
                AgentKeyHash: agentKeyHash,
                LastHostname: agentEntity.GetString("LastHostname"),
                LastIp: agentEntity.GetString("LastIp"),
                LastSeenUtc: agentEntity.GetDateTimeOffset("LastSeenUtc"),
                Version: agentEntity.GetString("Version")
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating agent with hash: {Hash}", agentKeyHash);
            return null;
        }
    }

    public async Task UpdateAgentMetadataAsync(Guid agentId, Guid tenantId, string? hostname, string? ipAddress, string? version, CancellationToken ct)
    {
        try
        {
            var partitionKey = tenantId.ToString().ToLowerInvariant();
            var rowKey = agentId.ToString();

            // Use merge to only update metadata fields, not overwrite entire entity
            var entity = new TableEntity(partitionKey, rowKey)
            {
                ["LastHostname"] = hostname,
                ["LastIp"] = ipAddress,
                ["Version"] = version,
                ["LastSeenUtc"] = DateTimeOffset.UtcNow
            };

            await _agentsTable.UpsertEntityAsync(entity, TableUpdateMode.Merge, ct);
            _logger.LogDebug("Updated agent metadata: AgentId={AgentId}, Hostname={Hostname}, IP={IP}", agentId, hostname, ipAddress);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update agent metadata for AgentId={AgentId}", agentId);
        }
    }

    public async Task UpsertAgentAsync(AgentAuthDto agent, CancellationToken ct)
    {
        try
        {
            var partitionKey = agent.TenantId.ToString().ToLowerInvariant();
            var rowKey = agent.AgentId.ToString();

            // Upsert main agent record using Merge mode to preserve any additional columns
            var agentEntity = new TableEntity(partitionKey, rowKey)
            {
                ["Name"] = agent.Name,
                // Store normalized hash to keep table storage consistent (normalize to upper-case)
                ["AgentKeyHash"] = (agent.AgentKeyHash ?? string.Empty).ToUpperInvariant(),
                ["LastHostname"] = agent.LastHostname,
                ["LastIp"] = agent.LastIp,
                ["LastSeenUtc"] = agent.LastSeenUtc,
                ["Version"] = agent.Version
            };

            await _agentsTable.UpsertEntityAsync(agentEntity, TableUpdateMode.Merge, ct);

            // Upsert hash index entry for fast lookups
            var normalizedHash = (agent.AgentKeyHash ?? string.Empty).ToUpperInvariant();
            var hashPartitionKey = normalizedHash.Length >= 4 ? normalizedHash[..4] : normalizedHash;
            var indexEntity = new TableEntity(hashPartitionKey, normalizedHash)
             {
                 ["AgentId"] = agent.AgentId.ToString(),
                 ["TenantId"] = agent.TenantId.ToString()
             };

            await _agentKeyHashIndex.UpsertEntityAsync(indexEntity, TableUpdateMode.Replace, ct);

            _logger.LogInformation("Upserted agent auth record: AgentId={AgentId}, TenantId={TenantId}", agent.AgentId, agent.TenantId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upsert agent: AgentId={AgentId}", agent.AgentId);
            throw;
        }
    }

    public async Task DeleteAgentAsync(Guid agentId, Guid tenantId, CancellationToken ct)
    {
        try
        {
            var partitionKey = tenantId.ToString().ToLowerInvariant();
            var rowKey = agentId.ToString();

            // Fetch agent to get hash for index deletion
            TableEntity? agentEntity;
            try
            {
                agentEntity = await _agentsTable.GetEntityAsync<TableEntity>(partitionKey, rowKey, cancellationToken: ct);
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
            {
                _logger.LogWarning("Agent not found for deletion: AgentId={AgentId}", agentId);
                return;
            }

            // Delete from main table
            await _agentsTable.DeleteEntityAsync(partitionKey, rowKey, cancellationToken: ct);

            // Delete from hash index
            if (agentEntity.TryGetValue("AgentKeyHash", out var hashObj))
            {
                var hash = hashObj.ToString()!;
                var normalizedHash = hash.ToUpperInvariant();
                var hashPartitionKey = normalizedHash.Length >= 4 ? normalizedHash[..4] : normalizedHash;
                await _agentKeyHashIndex.DeleteEntityAsync(hashPartitionKey, normalizedHash, cancellationToken: ct);
             }

             _logger.LogInformation("Deleted agent auth record: AgentId={AgentId}, TenantId={TenantId}", agentId, tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete agent: AgentId={AgentId}", agentId);
            throw;
        }
    }

    public async IAsyncEnumerable<AgentAuthDto> GetAgentsForTenantAsync(Guid tenantId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var partitionKey = tenantId.ToString().ToLowerInvariant();
        var filter = $"PartitionKey eq '{partitionKey}'";
        
        _logger.LogDebug("Querying agents for tenant: TenantId={TenantId}", tenantId);
        
        await foreach (var entity in _agentsTable.QueryAsync<TableEntity>(filter, cancellationToken: ct))
        {
            var agentId = Guid.Parse(entity.RowKey);
            
            yield return new AgentAuthDto(
                AgentId: agentId,
                TenantId: tenantId,
                Name: entity.GetString("Name") ?? "Agent",
                AgentKeyHash: entity.GetString("AgentKeyHash") ?? string.Empty,
                LastHostname: entity.GetString("LastHostname"),
                LastIp: entity.GetString("LastIp"),
                LastSeenUtc: entity.GetDateTimeOffset("LastSeenUtc"),
                Version: entity.GetString("Version")
            );
        }
    }
 }
