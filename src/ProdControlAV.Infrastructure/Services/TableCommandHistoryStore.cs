using Azure;
using Azure.Data.Tables;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace ProdControlAV.Infrastructure.Services
{
    /// <summary>
    /// Table Storage implementation for command execution history
    /// Partition Key: TenantId
    /// Row Key: ExecutionId (GUID for unique execution)
    /// </summary>
    public sealed class TableCommandHistoryStore : ICommandHistoryStore
    {
        private readonly TableClient _table;

        public TableCommandHistoryStore(TableServiceClient tableServiceClient) => 
            _table = tableServiceClient.GetTableClient("CommandHistory");

        public async Task RecordExecutionAsync(CommandHistoryDto history, CancellationToken ct)
        {
            var entity = new TableEntity(history.TenantId.ToString().ToLowerInvariant(), history.ExecutionId.ToString())
            {
                ["CommandId"] = history.CommandId.ToString(),
                ["DeviceId"] = history.DeviceId.ToString(),
                ["CommandName"] = history.CommandName,
                ["ExecutedUtc"] = history.ExecutedUtc,
                ["Success"] = history.Success,
                ["ErrorMessage"] = history.ErrorMessage,
                ["Response"] = history.Response,
                ["HttpStatusCode"] = history.HttpStatusCode,
                ["ExecutionTimeMs"] = history.ExecutionTimeMs
            };

            await _table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
        }

        public async IAsyncEnumerable<CommandHistoryDto> GetHistoryForCommandAsync(
            Guid tenantId, 
            Guid commandId, 
            [EnumeratorCancellation] CancellationToken ct)
        {
            var partitionKey = tenantId.ToString().ToLowerInvariant();
            var commandIdStr = commandId.ToString();
            
            var query = _table.QueryAsync<TableEntity>(
                e => e.PartitionKey == partitionKey && e["CommandId"].ToString() == commandIdStr,
                cancellationToken: ct);

            await foreach (var e in query)
            {
                yield return MapToDto(e);
            }
        }

        public async IAsyncEnumerable<CommandHistoryDto> GetHistoryForDeviceAsync(
            Guid tenantId, 
            Guid deviceId, 
            [EnumeratorCancellation] CancellationToken ct)
        {
            var partitionKey = tenantId.ToString().ToLowerInvariant();
            var deviceIdStr = deviceId.ToString();
            
            var query = _table.QueryAsync<TableEntity>(
                e => e.PartitionKey == partitionKey && e["DeviceId"].ToString() == deviceIdStr,
                cancellationToken: ct);

            await foreach (var e in query)
            {
                yield return MapToDto(e);
            }
        }

        public async IAsyncEnumerable<CommandHistoryDto> GetRecentHistoryForTenantAsync(
            Guid tenantId, 
            int days, 
            [EnumeratorCancellation] CancellationToken ct)
        {
            var partitionKey = tenantId.ToString().ToLowerInvariant();
            var cutoffDate = DateTimeOffset.UtcNow.AddDays(-days);
            
            var query = _table.QueryAsync<TableEntity>(
                e => e.PartitionKey == partitionKey && e.Timestamp >= cutoffDate,
                cancellationToken: ct);

            await foreach (var e in query)
            {
                yield return MapToDto(e);
            }
        }

        private static CommandHistoryDto MapToDto(TableEntity e)
        {
            return new CommandHistoryDto(
                Guid.Parse(e.RowKey),
                Guid.Parse(e["CommandId"].ToString()!),
                Guid.Parse(e.PartitionKey),
                Guid.Parse(e["DeviceId"].ToString()!),
                e["CommandName"].ToString()!,
                e["ExecutedUtc"] is DateTimeOffset executedUtc ? executedUtc : DateTimeOffset.UtcNow,
                e["Success"] is bool success && success,
                e.TryGetValue("ErrorMessage", out var error) ? error?.ToString() : null,
                e.TryGetValue("Response", out var response) ? response?.ToString() : null,
                e.TryGetValue("HttpStatusCode", out var statusCode) && statusCode != null ? Convert.ToInt32(statusCode) : null,
                e.TryGetValue("ExecutionTimeMs", out var execTime) && execTime != null ? Convert.ToDouble(execTime) : null
            );
        }
    }
}
