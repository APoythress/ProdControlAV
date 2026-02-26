using System;
using System.Threading;
using System.Threading.Tasks;

namespace ProdControlAV.Infrastructure.Services
{
    /// <summary>
    /// Per-device SMS notification state – tracks the last type of SMS sent (OFFLINE/ONLINE)
    /// so that ONLINE recovery notifications are only sent after a prior OFFLINE notification.
    /// </summary>
    public record DeviceSmsStateDto(
        Guid TenantId,
        Guid DeviceId,
        string? LastSentType,   // "OFFLINE" or "ONLINE", null if no SMS has been sent
        DateTimeOffset? LastSentUtc);

    public interface IDeviceSmsStateStore
    {
        Task<DeviceSmsStateDto?> GetAsync(Guid tenantId, Guid deviceId, CancellationToken ct);
        Task UpsertAsync(Guid tenantId, Guid deviceId, string lastSentType, DateTimeOffset lastSentUtc, CancellationToken ct);
    }

    /// <summary>
    /// Append-only log of every SMS sent. PartitionKey = TenantId,
    /// RowKey = yyyyMMddHHmmssfff-DeviceId-Type for time-sortable ordering.
    /// </summary>
    public interface ISmsNotificationLogStore
    {
        Task AppendAsync(
            Guid tenantId,
            Guid deviceId,
            string type,
            DateTimeOffset sentUtc,
            string? toPhoneMasked,
            string? providerMessageId,
            CancellationToken ct);
    }

    /// <summary>
    /// Aggregated per-tenant SMS counters (monthly bucket).
    /// PartitionKey = TenantId, RowKey = yyyyMM.
    /// </summary>
    public interface ITenantSmsUsageStore
    {
        /// <summary>Increment the SMS counter for the given tenant and period (yyyyMM).</summary>
        Task IncrementAsync(Guid tenantId, string type, CancellationToken ct);
    }
}
