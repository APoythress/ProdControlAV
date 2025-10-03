using System;
using System.Collections.Generic;

namespace ProdControlAV.WebApp.Models
{
    public record DeviceStatusDto(Guid DeviceId, string Status, int? LatencyMs, DateTimeOffset LastSeenUtc);
    public record StatusListDto(Guid TenantId, IReadOnlyList<DeviceStatusDto> Items, DateTimeOffset AsOfUtc);
}