using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProdControlAV.Infrastructure.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace ProdControlAV.API.Controllers;

[ApiController]
[Route("api/status")]
[Authorize(Policy = "TenantMember")]
public sealed class StatusController : ControllerBase
{
    private readonly IDeviceStatusStore _statusStore;
    private readonly IDeviceStore _deviceStore;
    private readonly ILogger<StatusController> _logger;
    
    public StatusController(IDeviceStatusStore statusStore, IDeviceStore deviceStore, ILogger<StatusController> logger)
    {
        _statusStore = statusStore;
        _deviceStore = deviceStore;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] StatusPostDto dto, CancellationToken ct)
    {
        // Validate tenant from claims
        var tenantIdClaim = User.FindFirst("tenant_id")?.Value ?? User.FindFirst("tenantId")?.Value;
        if (string.IsNullOrWhiteSpace(tenantIdClaim) || !Guid.TryParse(tenantIdClaim, out var tenantId))
            return Forbid();
        if (tenantId != dto.TenantId)
            return Forbid();

        var observedAt = dto.ObservedAt ?? DateTimeOffset.UtcNow;

        // Write status to DeviceStatus table (for backward compatibility)
        await _statusStore.UpsertAsync(dto.TenantId, dto.DeviceId, dto.Status, dto.LatencyMs, observedAt, ct);
        
        // Also write status to Devices table using merge mode for cost optimization
        await _deviceStore.UpsertStatusAsync(dto.TenantId, dto.DeviceId, dto.Status, observedAt, observedAt, ct);
        
        _logger.LogInformation("TableStorage Write: tenant={TenantId} device={DeviceId} status={Status}", 
            dto.TenantId, dto.DeviceId, dto.Status);
        return NoContent();
    }

    [HttpGet]
    public async Task<ActionResult<StatusListDto>> Get([FromQuery] Guid? tenantId, CancellationToken ct)
    {
        // Validate claims vs tenantId
        var tenantIdClaim = User.FindFirst("tenant_id")?.Value ?? User.FindFirst("tenantId")?.Value;
        if (string.IsNullOrWhiteSpace(tenantIdClaim) || !Guid.TryParse(tenantIdClaim, out var claimTenantId))
            return Forbid();
        
        // If tenantId is not provided in query, use the one from claims
        var effectiveTenantId = tenantId ?? claimTenantId;
        
        // Ensure the user can only access their own tenant's data
        if (claimTenantId != effectiveTenantId)
            return Forbid();

        var items = new List<DeviceStatusDto>();
        int readCount = 0;
        await foreach (var s in _statusStore.GetAllForTenantAsync(effectiveTenantId, ct))
        {
            items.Add(s);
            readCount++;
        }
        _logger.LogInformation("TableStorage Read: tenant={TenantId} count={Count}", effectiveTenantId, readCount);
        return Ok(new StatusListDto(effectiveTenantId, items, DateTimeOffset.UtcNow));
    }
}

public record StatusPostDto(Guid TenantId, Guid DeviceId, string Status, int? LatencyMs, DateTimeOffset? ObservedAt);
public record StatusListDto(Guid TenantId, IReadOnlyList<DeviceStatusDto> Items, DateTimeOffset AsOfUtc);
