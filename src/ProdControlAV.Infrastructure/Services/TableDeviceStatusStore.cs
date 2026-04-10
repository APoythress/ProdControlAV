using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;

namespace ProdControlAV.Infrastructure.Services
{
    public sealed class TableDeviceStatusStore : IDeviceStatusStore
    {
        private readonly TableClient _table;
        public TableDeviceStatusStore(TableServiceClient tableServiceClient) => 
            _table = tableServiceClient.GetTableClient("DeviceStatus");

        public async Task UpsertAsync(Guid tenantId, Guid deviceId, string status, int? latencyMs, DateTimeOffset ts, CancellationToken ct)
        {
            var entity = new TableEntity(tenantId.ToString().ToLowerInvariant(), deviceId.ToString())
            {
                ["Status"] = status,
                ["LatencyMs"] = latencyMs,
                ["LastSeenUtc"] = ts
            };
            // Use Merge to preserve any other columns that may exist in the entity
            await _table.UpsertEntityAsync(entity, TableUpdateMode.Merge, ct);
        }

        public async IAsyncEnumerable<DeviceStatusDto> GetAllForTenantAsync(Guid tenantId, [EnumeratorCancellation] CancellationToken ct)
        {
            var query = _table.QueryAsync<TableEntity>(x => x.PartitionKey == tenantId.ToString().ToLowerInvariant(), cancellationToken: ct);
            await foreach (var e in query)
            {
                yield return new DeviceStatusDto(
                    Guid.Parse(e.RowKey),
                    (string)e["Status"],
                    e.ContainsKey("LatencyMs") ? (int?)Convert.ToInt32(e["LatencyMs"]) : null,
                    (DateTimeOffset)e["LastSeenUtc"]
                );
            }
        }

        public async Task<DeviceStatusDto?> GetDeviceStatusAsync(Guid tenantId, Guid deviceId, CancellationToken ct)
        {
            try
            {
                var response = await _table.GetEntityAsync<TableEntity>(
                    tenantId.ToString().ToLowerInvariant(),
                    deviceId.ToString(),
                    cancellationToken: ct);

                var e = response.Value;
                return new DeviceStatusDto(
                    Guid.Parse(e.RowKey),
                    (string)e["Status"],
                    e.ContainsKey("LatencyMs") ? (int?)Convert.ToInt32(e["LatencyMs"]) : null,
                    (DateTimeOffset)e["LastSeenUtc"]
                );
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }
    }
}

