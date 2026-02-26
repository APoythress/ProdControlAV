using Azure;
using Azure.Data.Tables;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ProdControlAV.Infrastructure.Services
{
    /// <summary>
    /// Azure Table Storage implementation of <see cref="ITenantSmsUsageStore"/>.
    /// Table: TenantSmsUsage | PartitionKey = TenantId | RowKey = yyyyMM (monthly bucket)
    /// </summary>
    public sealed class TableTenantSmsUsageStore : ITenantSmsUsageStore
    {
        private readonly TableClient _table;

        public TableTenantSmsUsageStore(TableClient table) => _table = table;

        public async Task IncrementAsync(Guid tenantId, string type, CancellationToken ct)
        {
            var partitionKey = tenantId.ToString().ToLowerInvariant();
            var rowKey = DateTimeOffset.UtcNow.ToString("yyyyMM");

            // Read current counters
            int countTotal = 0, countOffline = 0, countOnline = 0;
            try
            {
                var response = await _table.GetEntityAsync<TableEntity>(partitionKey, rowKey, cancellationToken: ct);
                var e = response.Value;
                if (e.TryGetValue("CountTotal", out var vt) && vt != null) int.TryParse(Convert.ToString(vt), out countTotal);
                if (e.TryGetValue("CountOffline", out var vo) && vo != null) int.TryParse(Convert.ToString(vo), out countOffline);
                if (e.TryGetValue("CountOnline", out var von) && von != null) int.TryParse(Convert.ToString(von), out countOnline);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // No existing entry – start from zero
            }

            countTotal++;
            if (string.Equals(type, "OFFLINE", StringComparison.OrdinalIgnoreCase))
                countOffline++;
            else if (string.Equals(type, "ONLINE", StringComparison.OrdinalIgnoreCase))
                countOnline++;

            var entity = new TableEntity(partitionKey, rowKey)
            {
                ["CountTotal"] = countTotal,
                ["CountOffline"] = countOffline,
                ["CountOnline"] = countOnline,
                ["LastUpdatedUtc"] = DateTimeOffset.UtcNow
            };

            await _table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
        }
    }
}
