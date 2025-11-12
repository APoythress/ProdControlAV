using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ProdControlAV.Infrastructure.Services
{
    public record DeviceStatusDto(Guid DeviceId, string Status, int? LatencyMs, DateTimeOffset LastSeenUtc);

    public interface IDeviceStatusStore
    {
        Task UpsertAsync(Guid tenantId, Guid deviceId, string status, int? latencyMs, DateTimeOffset ts, CancellationToken ct);
        IAsyncEnumerable<DeviceStatusDto> GetAllForTenantAsync(Guid tenantId, CancellationToken ct);
        Task<DeviceStatusDto?> GetDeviceStatusAsync(Guid tenantId, Guid deviceId, CancellationToken ct);
    }
}

