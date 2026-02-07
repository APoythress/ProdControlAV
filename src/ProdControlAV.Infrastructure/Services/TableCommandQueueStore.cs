using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace ProdControlAV.Infrastructure.Services
{
    /// <summary>
    /// Table Storage implementation for command queue
    /// Partition Key: TenantId
    /// Row Key: CommandId_QueuedUtc (for chronological ordering)
    /// </summary>
    public sealed class TableCommandQueueStore : ICommandQueueStore
    {
        private readonly TableClient _table;
        private readonly ILogger<TableCommandQueueStore>? _logger;

        public TableCommandQueueStore(TableClient table, ILogger<TableCommandQueueStore>? logger = null)
        {
            _table = table;
            _logger = logger;
        }

        public async Task EnqueueAsync(CommandQueueDto command, CancellationToken ct)
        {
            var rowKey = $"{command.CommandId:N}_{command.QueuedUtc:yyyyMMddHHmmssfff}";
            
            var entity = new TableEntity(command.TenantId.ToString().ToLowerInvariant(), rowKey)
            {
                ["CommandId"] = command.CommandId.ToString(),
                ["DeviceId"] = command.DeviceId.ToString(),
                ["CommandName"] = command.CommandName,
                ["CommandType"] = command.CommandType,
                ["CommandData"] = command.CommandData,
                ["HttpMethod"] = command.HttpMethod,
                ["AtemFunction"] = command.AtemFunction,
                ["AtemInputId"] = command.AtemInputId,
                ["RequestBody"] = command.RequestBody,
                ["RequestHeaders"] = command.RequestHeaders,
                ["QueuedUtc"] = command.QueuedUtc,
                ["QueuedByUserId"] = command.QueuedByUserId.ToString(),
                ["DeviceIp"] = command.DeviceIp,
                ["DevicePort"] = command.DevicePort,
                ["DeviceType"] = command.DeviceType,
                ["MonitorRecordingStatus"] = command.MonitorRecordingStatus,
                ["StatusEndpoint"] = command.StatusEndpoint,
                ["StatusPollingIntervalSeconds"] = command.StatusPollingIntervalSeconds,
                ["Status"] = command.Status,
                ["AttemptCount"] = command.AttemptCount
            };

            await _table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
        }

        // Replacements for the query methods using OData filter strings
        public async IAsyncEnumerable<CommandQueueDto> GetPendingForDeviceAsync(
            Guid tenantId,
            Guid deviceId,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var partitionKey = tenantId.ToString().ToLowerInvariant();
            var deviceIdStr = deviceId.ToString();
        
            var filter = $"PartitionKey eq '{partitionKey}' and DeviceId eq '{deviceIdStr}' and Status eq 'Pending'";
        
            var query = _table.QueryAsync<TableEntity>(filter, cancellationToken: ct);
        
            await foreach (var e in query)
            {
                yield return MapToDto(e);
            }
        }
        
        public async IAsyncEnumerable<CommandQueueDto> GetPendingForTenantAsync(
            Guid tenantId,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var partitionKey = tenantId.ToString().ToLowerInvariant();
        
            var filter = $"PartitionKey eq '{partitionKey}' and Status eq 'Pending'";
        
            var query = _table.QueryAsync<TableEntity>(filter, cancellationToken: ct);
        
            await foreach (var e in query)
            {
                yield return MapToDto(e);
            }
        }
        
        public async IAsyncEnumerable<CommandQueueDto> GetStuckProcessingCommandsAsync(
            Guid tenantId,
            TimeSpan timeout,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var partitionKey = tenantId.ToString().ToLowerInvariant();
            var cutoffTime = DateTimeOffset.UtcNow.Add(-timeout);
        
            // Get all processing commands for the tenant
            var filter = $"PartitionKey eq '{partitionKey}' and Status eq 'Processing'";
        
            var query = _table.QueryAsync<TableEntity>(filter, cancellationToken: ct);
        
            await foreach (var e in query)
            {
                // Check if ProcessingStartedUtc is older than cutoff
                if (e.TryGetValue("ProcessingStartedUtc", out var startedValue) && startedValue != null)
                {
                    var isStuck = false;
                    try
                    {
                        var startedUtc = (DateTimeOffset)startedValue;
                        if (startedUtc < cutoffTime)
                        {
                            isStuck = true;
                        }
                    }
                    catch (InvalidCastException ex)
                    {
                        // Cannot parse ProcessingStartedUtc, skip this command
                        _logger?.LogWarning(ex, "Failed to parse ProcessingStartedUtc for stuck command check");
                    }
                    catch (FormatException ex)
                    {
                        // Cannot parse ProcessingStartedUtc, skip this command
                        _logger?.LogWarning(ex, "Failed to parse ProcessingStartedUtc for stuck command check");
                    }
                    
                    if (isStuck)
                    {
                        yield return MapToDto(e);
                    }
                }
            }
        }
        
        public async Task ResetToPendingAsync(Guid tenantId, Guid commandId, CancellationToken ct)
        {
            var partitionKey = tenantId.ToString().ToLowerInvariant();
            var commandIdStr = commandId.ToString();
        
            var filter = $"PartitionKey eq '{partitionKey}' and CommandId eq '{commandIdStr}'";
        
            var query = _table.QueryAsync<TableEntity>(filter, maxPerPage: 1, cancellationToken: ct);
        
            await foreach (var entity in query)
            {
                entity["Status"] = "Pending";
                entity["ProcessingStartedUtc"] = null;
                await _table.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, ct);
                return;
            }
        }
        
        public async Task MarkAsProcessingAsync(Guid tenantId, Guid commandId, CancellationToken ct)
        {
            var partitionKey = tenantId.ToString().ToLowerInvariant();
            var commandIdStr = commandId.ToString();
        
            var filter = $"PartitionKey eq '{partitionKey}' and CommandId eq '{commandIdStr}'";
        
            var query = _table.QueryAsync<TableEntity>(filter, maxPerPage: 1, cancellationToken: ct);
        
            await foreach (var entity in query)
            {
                // Increment attempt count with comprehensive error handling
                int attemptCount = 0;
                if (entity.TryGetValue("AttemptCount", out var attemptValue) && attemptValue != null)
                {
                    try
                    {
                        attemptCount = Convert.ToInt32(attemptValue);
                    }
                    catch (InvalidCastException ex)
                    {
                        _logger?.LogWarning(ex, "Failed to parse AttemptCount in MarkAsProcessingAsync, defaulting to 0");
                    }
                    catch (FormatException ex)
                    {
                        _logger?.LogWarning(ex, "Failed to parse AttemptCount in MarkAsProcessingAsync, defaulting to 0");
                    }
                    catch (OverflowException ex)
                    {
                        _logger?.LogWarning(ex, "AttemptCount overflow in MarkAsProcessingAsync, defaulting to 0");
                    }
                }
                attemptCount++;
                
                entity["AttemptCount"] = attemptCount;
                entity["Status"] = "Processing";
                entity["ProcessingStartedUtc"] = DateTimeOffset.UtcNow;
                await _table.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, ct);
                return;
            }
        }
        
        public async Task MarkAsSucceededAsync(Guid tenantId, Guid commandId, CancellationToken ct)
        {
            var partitionKey = tenantId.ToString().ToLowerInvariant();
            var commandIdStr = commandId.ToString();
        
            var filter = $"PartitionKey eq '{partitionKey}' and CommandId eq '{commandIdStr}'";
        
            var query = _table.QueryAsync<TableEntity>(filter, maxPerPage: 1, cancellationToken: ct);
        
            await foreach (var entity in query)
            {
                entity["Status"] = "Succeeded";
                entity["CompletedUtc"] = DateTimeOffset.UtcNow;
                await _table.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, ct);
                _logger?.LogInformation("Marked command {CommandId} as Succeeded", commandId);
                return;
            }
        }
        
        public async Task MarkAsFailedAsync(Guid tenantId, Guid commandId, CancellationToken ct)
        {
            var partitionKey = tenantId.ToString().ToLowerInvariant();
            var commandIdStr = commandId.ToString();
        
            var filter = $"PartitionKey eq '{partitionKey}' and CommandId eq '{commandIdStr}'";
        
            var query = _table.QueryAsync<TableEntity>(filter, maxPerPage: 1, cancellationToken: ct);
        
            await foreach (var entity in query)
            {
                entity["Status"] = "Failed";
                entity["FailedUtc"] = DateTimeOffset.UtcNow;
                await _table.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, ct);
                _logger?.LogInformation("Marked command {CommandId} as Failed", commandId);
                return;
            }
        }
        
        public async Task DequeueAsync(Guid tenantId, Guid commandId, CancellationToken ct)
        {
            var partitionKey = tenantId.ToString().ToLowerInvariant();
            var commandIdStr = commandId.ToString();
        
            var filter = $"PartitionKey eq '{partitionKey}' and CommandId eq '{commandIdStr}'";
        
            var query = _table.QueryAsync<TableEntity>(filter, maxPerPage: 1, cancellationToken: ct);
        
            await foreach (var entity in query)
            {
                try
                {
                    await _table.DeleteEntityAsync(entity.PartitionKey, entity.RowKey, cancellationToken: ct);
                }
                catch (RequestFailedException ex) when (ex.Status == 404)
                {
                    // Already deleted, ignore
                }
                return;
            }
        }
        
        private CommandQueueDto MapToDto(TableEntity e)
        {
            bool monitorRecordingStatus = false;
            if (e.TryGetValue("MonitorRecordingStatus", out var monitor) && monitor != null)
            {
                try 
                { 
                    monitorRecordingStatus = Convert.ToBoolean(monitor); 
                } 
                catch (InvalidCastException ex)
                {
                    _logger?.LogWarning(ex, "Failed to parse MonitorRecordingStatus, defaulting to false");
                }
                catch (FormatException ex)
                {
                    _logger?.LogWarning(ex, "Failed to parse MonitorRecordingStatus, defaulting to false");
                }
            }
            
            int attemptCount = 0;
            if (e.TryGetValue("AttemptCount", out var attemptValue) && attemptValue != null)
            {
                try 
                { 
                    attemptCount = Convert.ToInt32(attemptValue); 
                } 
                catch (InvalidCastException ex)
                {
                    _logger?.LogWarning(ex, "Failed to parse AttemptCount, defaulting to 0");
                }
                catch (FormatException ex)
                {
                    _logger?.LogWarning(ex, "Failed to parse AttemptCount, defaulting to 0");
                }
                catch (OverflowException ex)
                {
                    _logger?.LogWarning(ex, "AttemptCount overflow, defaulting to 0");
                }
            }
            
            return new CommandQueueDto(
                Guid.Parse(e["CommandId"].ToString()!),
                Guid.Parse(e.PartitionKey),
                Guid.Parse(e["DeviceId"].ToString()!),
                e["CommandName"].ToString()!,
                e["CommandType"].ToString()!,
                e.TryGetValue("CommandData", out var cmdData) ? cmdData?.ToString() : null,
                e.TryGetValue("HttpMethod", out var method) ? method?.ToString() : null,
                e.TryGetValue("RequestBody", out var body) ? body?.ToString() : null,
                e.TryGetValue("RequestHeaders", out var headers) ? headers?.ToString() : null,
                e["QueuedUtc"] is DateTimeOffset queuedUtc ? queuedUtc : DateTimeOffset.UtcNow,
                Guid.Parse(e["QueuedByUserId"].ToString()!),
                e.TryGetValue("DeviceIp", out var ip) ? ip?.ToString() : null,
                e.TryGetValue("DevicePort", out var port) && port != null ? Convert.ToInt32(port) : null,
                e.TryGetValue("DeviceType", out var dtype) ? dtype?.ToString() : null,
                monitorRecordingStatus,
                e.TryGetValue("StatusEndpoint", out var endpoint) ? endpoint?.ToString() : null,
                e.TryGetValue("AtemFunction", out var atemFunction) ? atemFunction?.ToString() : null,
                e.TryGetValue("AtemInputId", out var atemInputId) && atemInputId != null ? Convert.ToInt32(atemInputId) : 0,
                e.TryGetValue("AtemTransitionRate", out var atemTransitionRate) && atemTransitionRate != null ? Convert.ToInt32(atemTransitionRate) : 60,
                e.TryGetValue("AtemMacroId", out var atemMacroId) && atemMacroId != null ? Convert.ToInt32(atemMacroId) : 0,
                e.TryGetValue("StatusPollingIntervalSeconds", out var interval) && interval != null ? Convert.ToInt32(interval) : 60,
                e.TryGetValue("Status", out var status) ? status?.ToString() ?? "Pending" : "Pending",
                attemptCount
            );
        }
    }
}
