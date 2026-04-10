using Azure.Data.Tables;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ProdControlAV.Infrastructure.Services
{
    /// <summary>
    /// Azure Table Storage implementation of <see cref="ISmsNotificationLogStore"/>.
    /// Table: SmsNotificationLog | PartitionKey = TenantId | RowKey = yyyyMMddHHmmssfff-DeviceId-Type
    /// </summary>
    public sealed class TableSmsNotificationLogStore : ISmsNotificationLogStore
    {
        private readonly TableClient _table;

        public TableSmsNotificationLogStore(TableServiceClient tableServiceClient) => 
            _table = tableServiceClient.GetTableClient("SmsNotificationLog");

        public async Task AppendAsync(
            Guid tenantId,
            Guid deviceId,
            string type,
            DateTimeOffset sentUtc,
            string? toPhoneMasked,
            string? providerMessageId,
            CancellationToken ct)
        {
            var rowKey = $"{sentUtc:yyyyMMddHHmmssfff}-{deviceId}-{type}";
            var entity = new TableEntity(tenantId.ToString().ToLowerInvariant(), rowKey)
            {
                ["TenantId"] = tenantId.ToString(),
                ["DeviceId"] = deviceId.ToString(),
                ["Type"] = type,
                ["SentUtc"] = sentUtc
            };

            if (!string.IsNullOrEmpty(toPhoneMasked))
                entity["ToPhoneMasked"] = toPhoneMasked;

            if (!string.IsNullOrEmpty(providerMessageId))
                entity["ProviderMessageId"] = providerMessageId;

            await _table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
        }
    }
}
