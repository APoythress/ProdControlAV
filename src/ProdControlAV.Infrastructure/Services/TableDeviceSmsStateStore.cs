using System;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;

namespace ProdControlAV.Infrastructure.Services
{
    /// <summary>
    /// Azure Table Storage implementation of <see cref="IDeviceSmsStateStore"/>.
    /// Table: DeviceSmsState | PartitionKey = TenantId | RowKey = DeviceId
    /// </summary>
    public sealed class TableDeviceSmsStateStore : IDeviceSmsStateStore
    {
        private readonly TableClient _table;

        public TableDeviceSmsStateStore(TableServiceClient svc) =>
            _table = svc.GetTableClient("DeviceSmsState");

        public async Task<DeviceSmsStateDto?> GetAsync(Guid tenantId, Guid deviceId, CancellationToken ct)
        {
            try
            {
                var response = await _table.GetEntityAsync<TableEntity>(
                    tenantId.ToString().ToLowerInvariant(),
                    deviceId.ToString(),
                    cancellationToken: ct);

                var e = response.Value;
                string? lastSentType = e.ContainsKey("LastSentType") ? Convert.ToString(e["LastSentType"]) : null;

                DateTimeOffset? lastSentUtc = null;
                if (e.TryGetValue("LastSentUtc", out var v) && v != null)
                {
                    if (v is DateTimeOffset dto) lastSentUtc = dto;
                    else if (v is DateTime dt) lastSentUtc = new DateTimeOffset(dt);
                    else if (v is string s && DateTimeOffset.TryParse(s, out var p)) lastSentUtc = p;
                }

                return new DeviceSmsStateDto(tenantId, deviceId, lastSentType, lastSentUtc);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task UpsertAsync(Guid tenantId, Guid deviceId, string lastSentType, DateTimeOffset lastSentUtc, CancellationToken ct)
        {
            var entity = new TableEntity(tenantId.ToString().ToLowerInvariant(), deviceId.ToString())
            {
                ["LastSentType"] = lastSentType,
                ["LastSentUtc"] = lastSentUtc
            };
            await _table.UpsertEntityAsync(entity, TableUpdateMode.Merge, ct);
        }
    }
}
