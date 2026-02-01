using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ProdControlAV.Infrastructure.Services
{
    /// <summary>
    /// Represents ATEM device state stored in Azure Table Storage
    /// </summary>
    public record AtemStateDto(
        Guid DeviceId,
        Guid TenantId,
        List<AtemInputDto> Inputs,
        Dictionary<string, long?> CurrentSources, // Key: Destination (Program, Aux1, etc.), Value: Current Input ID
        DateTimeOffset LastUpdatedUtc);

    public record AtemInputDto(
        long InputId,
        string Name,
        string Type);

    /// <summary>
    /// Store for ATEM device state in Azure Table Storage
    /// </summary>
    public interface IAtemStateStore
    {
        /// <summary>
        /// Upsert ATEM state for a device
        /// </summary>
        Task UpsertStateAsync(Guid tenantId, Guid deviceId, List<AtemInputDto> inputs, 
            Dictionary<string, long?> currentSources, CancellationToken ct);

        /// <summary>
        /// Get ATEM state for a device
        /// </summary>
        Task<AtemStateDto?> GetStateAsync(Guid tenantId, Guid deviceId, CancellationToken ct);

        /// <summary>
        /// Delete ATEM state for a device
        /// </summary>
        Task DeleteAsync(Guid tenantId, Guid deviceId, CancellationToken ct);
    }
}
