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
    private readonly IDeviceStatusStore _store;
    private readonly ILogger<StatusController> _logger;
    public StatusController(IDeviceStatusStore store, ILogger<StatusController> logger)
    {
        _store = store;
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

        await _store.UpsertAsync(dto.TenantId, dto.DeviceId, dto.Status, dto.LatencyMs, dto.ObservedAt ?? DateTimeOffset.UtcNow, ct);
        _logger.LogInformation("TableStorage Write: tenant={TenantId} device={DeviceId}", dto.TenantId, dto.DeviceId);
        return NoContent();
    }

    [HttpGet]
    public async Task<ActionResult<StatusListDto>> Get([FromQuery] Guid tenantId, CancellationToken ct)
    {
        // Validate claims vs tenantId
        var tenantIdClaim = User.FindFirst("tenant_id")?.Value ?? User.FindFirst("tenantId")?.Value;
        if (string.IsNullOrWhiteSpace(tenantIdClaim) || !Guid.TryParse(tenantIdClaim, out var claimTenantId))
            return Forbid();
        if (claimTenantId != tenantId)
            return Forbid();

        var items = new List<DeviceStatusDto>();
        int readCount = 0;
        await foreach (var s in _store.GetAllForTenantAsync(tenantId, ct))
        {
            items.Add(s);
            readCount++;
        }
        _logger.LogInformation("TableStorage Read: tenant={TenantId} count={Count}", tenantId, readCount);
        return Ok(new StatusListDto(tenantId, items, DateTimeOffset.UtcNow));
    }
}

public record StatusPostDto(Guid TenantId, Guid DeviceId, string Status, int? LatencyMs, DateTimeOffset? ObservedAt);
public record StatusListDto(Guid TenantId, IReadOnlyList<DeviceStatusDto> Items, DateTimeOffset AsOfUtc);
