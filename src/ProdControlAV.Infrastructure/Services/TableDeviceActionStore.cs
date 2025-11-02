using Azure;
using Azure.Data.Tables;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ProdControlAV.Infrastructure.Services
{
    public sealed class TableDeviceActionStore : IDeviceActionStore
    {
        private readonly TableClient _table;
        public TableDeviceActionStore(TableClient table) => _table = table;

        public async Task UpsertAsync(Guid tenantId, Guid actionId, Guid deviceId, string actionName, CancellationToken ct)
        {
            var entity = new TableEntity(tenantId.ToString().ToLowerInvariant(), actionId.ToString())
            {
                ["DeviceId"] = deviceId.ToString(),
                ["ActionName"] = actionName
            };
            // Use Merge to preserve any other columns that may exist in the entity
            await _table.UpsertEntityAsync(entity, TableUpdateMode.Merge, ct);
        }

        public async Task DeleteAsync(Guid tenantId, Guid actionId, CancellationToken ct)
        {
            try
            {
                await _table.DeleteEntityAsync(tenantId.ToString().ToLowerInvariant(), actionId.ToString(), cancellationToken: ct);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Entity already deleted, ignore
            }
        }

        public async IAsyncEnumerable<DeviceActionDto> GetAllForTenantAsync(Guid tenantId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            var query = _table.QueryAsync<TableEntity>(x => x.PartitionKey == tenantId.ToString().ToLowerInvariant(), cancellationToken: ct);
            await foreach (var e in query)
            {
                yield return new DeviceActionDto(
                    Guid.Parse(e.RowKey),
                    Guid.Parse((string)e["DeviceId"]),
                    (string)e["ActionName"],
                    tenantId
                );
            }
        }
    }
}
