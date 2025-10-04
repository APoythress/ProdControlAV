using Azure;
using Azure.Data.Tables;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ProdControlAV.Infrastructure.Services
{
    public sealed class TableDeviceStore : IDeviceStore
    {
        private readonly TableClient _table;
        public TableDeviceStore(TableClient table) => _table = table;

        public async Task UpsertAsync(Guid tenantId, Guid deviceId, string name, string ipAddress, string type, 
            DateTimeOffset createdUtc, string? model, string? brand, string? location, bool allowTelNet, int port, CancellationToken ct)
        {
            var entity = new TableEntity(tenantId.ToString().ToLowerInvariant(), deviceId.ToString())
            {
                ["Name"] = name,
                ["IpAddress"] = ipAddress,
                ["Type"] = type,
                ["CreatedUtc"] = createdUtc,
                ["Model"] = model ?? "",
                ["Brand"] = brand ?? "",
                ["Location"] = location ?? "",
                ["AllowTelNet"] = allowTelNet,
                ["Port"] = port
            };
            await _table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
        }

        public async Task DeleteAsync(Guid tenantId, Guid deviceId, CancellationToken ct)
        {
            try
            {
                await _table.DeleteEntityAsync(tenantId.ToString().ToLowerInvariant(), deviceId.ToString(), cancellationToken: ct);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Entity already deleted, ignore
            }
        }

        public async IAsyncEnumerable<DeviceDto> GetAllForTenantAsync(Guid tenantId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            var query = _table.QueryAsync<TableEntity>(x => x.PartitionKey == tenantId.ToString().ToLowerInvariant(), cancellationToken: ct);
            await foreach (var e in query)
            {
                yield return new DeviceDto(
                    Guid.Parse(e.RowKey),
                    (string)e["Name"],
                    (string)e["IpAddress"],
                    (string)e["Type"],
                    tenantId,
                    (DateTimeOffset)e["CreatedUtc"],
                    e.ContainsKey("Model") ? (string)e["Model"] : null,
                    e.ContainsKey("Brand") ? (string)e["Brand"] : null,
                    e.ContainsKey("Location") ? (string)e["Location"] : null,
                    e.ContainsKey("AllowTelNet") && (bool)e["AllowTelNet"],
                    e.ContainsKey("Port") ? Convert.ToInt32(e["Port"]) : 80
                );
            }
        }
    }
}
