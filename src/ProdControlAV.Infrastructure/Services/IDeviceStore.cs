using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ProdControlAV.Infrastructure.Services
{
    public record DeviceDto(
        Guid Id, 
        string Name, 
        string IpAddress, 
        string Type, 
        Guid TenantId, 
        DateTimeOffset CreatedUtc,
        string? Model,
        string? Brand,
        string? Location,
        bool AllowTelNet,
        int Port,
        string? Status = null,
        DateTimeOffset? LastSeenUtc = null,
        DateTimeOffset? LastPolledUtc = null,
        double? HealthMetric = null);

    public interface IDeviceStore
    {
        Task UpsertAsync(Guid tenantId, Guid deviceId, string name, string ipAddress, string type, 
            DateTimeOffset createdUtc, string? model, string? brand, string? location, bool allowTelNet, int port, CancellationToken ct);
        Task UpsertStatusAsync(Guid tenantId, Guid deviceId, string status, DateTimeOffset lastSeenUtc, DateTimeOffset lastPolledUtc, CancellationToken ct);
        Task DeleteAsync(Guid tenantId, Guid deviceId, CancellationToken ct);
        IAsyncEnumerable<DeviceDto> GetAllForTenantAsync(Guid tenantId, CancellationToken ct);
    }

    public record DeviceActionDto(
        Guid ActionId,
        Guid DeviceId,
        string ActionName,
        Guid TenantId);

    public interface IDeviceActionStore
    {
        Task UpsertAsync(Guid tenantId, Guid actionId, Guid deviceId, string actionName, CancellationToken ct);
        Task DeleteAsync(Guid tenantId, Guid actionId, CancellationToken ct);
        IAsyncEnumerable<DeviceActionDto> GetAllForTenantAsync(Guid tenantId, CancellationToken ct);
    }
}
