using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProdControlAV.API.Models;
using ProdControlAV.API.Services;
using ProdControlAV.Core.Interfaces;
using ProdControlAV.Core.Models;
using ProdControlAV.Infrastructure.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ProdControlAV.API.Controllers;

/// <summary>
/// Controller for managing command definitions (SQL DB) and triggering execution (Table Storage queue)
/// </summary>
[ApiController]
[Route("api/commands")]
[Authorize(Policy = "Admin")]
public class CommandController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantProvider _tenant;
    private readonly ICommandQueueStore _queueStore;
    private readonly IDeviceStatusStore _deviceStatusStore;

    public CommandController(
        ITenantProvider tenant, 
        AppDbContext db, 
        ICommandQueueStore queueStore,
        IDeviceStatusStore deviceStatusStore)
    {
        _tenant = tenant;
        _db = db;
        _queueStore = queueStore;
        _deviceStatusStore = deviceStatusStore;
    }

    /// <summary>
    /// Get all commands for the current tenant
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var commands = await _db.Commands
            .AsNoTracking()
            .OrderBy(c => c.CommandName)
            .ToListAsync(ct);

        return Ok(commands);
    }

    /// <summary>
    /// Get commands for a specific device
    /// </summary>
    [HttpGet("device/{deviceId:guid}")]
    public async Task<IActionResult> GetForDevice(Guid deviceId, CancellationToken ct)
    {
        var commands = await _db.Commands
            .AsNoTracking()
            .Where(c => c.DeviceId == deviceId)
            .OrderBy(c => c.CommandName)
            .ToListAsync(ct);

        return Ok(commands);
    }

    /// <summary>
    /// Get a specific command by ID
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Command>> Get(Guid id, CancellationToken ct)
    {
        var command = await _db.Commands.FindAsync(new object[] { id }, ct);
        return command is not null ? Ok(command) : NotFound();
    }

    /// <summary>
    /// Create a new command definition
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<Command>> Create([FromBody] CreateCommandDto dto, CancellationToken ct)
    {
        if (dto.DeviceId == Guid.Empty)
            return BadRequest(new { error = "deviceId is required" });

        if (string.IsNullOrWhiteSpace(dto.CommandName))
            return BadRequest(new { error = "commandName is required" });

        if (string.IsNullOrWhiteSpace(dto.CommandType))
            return BadRequest(new { error = "commandType is required" });

        // Validate device exists and belongs to tenant
        var device = await _db.Devices.FindAsync(new object[] { dto.DeviceId }, ct);
        if (device is null)
            return NotFound(new { error = "device not found" });

        var command = new Command
        {
            CommandId = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            DeviceId = dto.DeviceId,
            CommandName = dto.CommandName.Trim(),
            Description = dto.Description?.Trim(),
            CommandType = dto.CommandType.Trim(),
            CommandData = dto.CommandData?.Trim(),
            HttpMethod = dto.HttpMethod?.Trim() ?? "POST",
            RequestBody = dto.RequestBody?.Trim(),
            RequestHeaders = dto.RequestHeaders?.Trim(),
            RequireDeviceOnline = dto.RequireDeviceOnline ?? true,
            MonitorRecordingStatus = dto.MonitorRecordingStatus ?? false,
            StatusEndpoint = dto.StatusEndpoint?.Trim(),
            StatusPollingIntervalSeconds = dto.StatusPollingIntervalSeconds ?? 60,
            CreatedUtc = DateTimeOffset.UtcNow
        };

        _db.Commands.Add(command);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = command.CommandId }, command);
    }

    /// <summary>
    /// Update an existing command definition
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCommandDto dto, CancellationToken ct)
    {
        var command = await _db.Commands.FindAsync(new object[] { id }, ct);
        if (command is null)
            return NotFound();

        if (!string.IsNullOrWhiteSpace(dto.CommandName))
            command.CommandName = dto.CommandName.Trim();
        
        if (dto.Description is not null)
            command.Description = dto.Description.Trim();
        
        if (!string.IsNullOrWhiteSpace(dto.CommandType))
            command.CommandType = dto.CommandType.Trim();
        
        if (dto.CommandData is not null)
            command.CommandData = dto.CommandData.Trim();
        
        if (dto.HttpMethod is not null)
            command.HttpMethod = dto.HttpMethod.Trim();
        
        if (dto.RequestBody is not null)
            command.RequestBody = dto.RequestBody.Trim();
        
        if (dto.RequestHeaders is not null)
            command.RequestHeaders = dto.RequestHeaders.Trim();
        
        if (dto.RequireDeviceOnline.HasValue)
            command.RequireDeviceOnline = dto.RequireDeviceOnline.Value;
        
        if (dto.MonitorRecordingStatus.HasValue)
            command.MonitorRecordingStatus = dto.MonitorRecordingStatus.Value;
        
        if (dto.StatusEndpoint is not null)
            command.StatusEndpoint = dto.StatusEndpoint.Trim();
        
        if (dto.StatusPollingIntervalSeconds.HasValue)
            command.StatusPollingIntervalSeconds = dto.StatusPollingIntervalSeconds.Value;

        command.UpdatedUtc = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);

        return Ok(command);
    }

    /// <summary>
    /// Delete a command definition
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var command = await _db.Commands.FindAsync(new object[] { id }, ct);
        if (command is null)
            return NotFound();

        _db.Commands.Remove(command);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    /// <summary>
    /// Trigger command execution by queueing it to Table Storage
    /// </summary>
    [HttpPost("{id:guid}/trigger")]
    public async Task<IActionResult> Trigger(Guid id, CancellationToken ct)
    {
        var tenantId = _tenant.TenantId;
        if (tenantId == Guid.Empty)
            return Unauthorized(new { error = "missing_or_invalid_tenant" });

        // Get command from SQL DB
        var command = await _db.Commands.FindAsync(new object[] { id }, ct);
        if (command is null)
            return NotFound(new { error = "command not found" });

        // Get device info from SQL DB
        var device = await _db.Devices.FindAsync(new object[] { command.DeviceId }, ct);
        if (device is null)
            return NotFound(new { error = "device not found" });

        // Check device online status if required
        if (command.RequireDeviceOnline)
        {
            var deviceStatus = await _deviceStatusStore.GetDeviceStatusAsync(tenantId, device.Id, ct);
            if (deviceStatus is null || deviceStatus.Status != "online")
            {
                return BadRequest(new { 
                    error = "device_offline", 
                    message = "Command requires device to be online, but device is currently offline" 
                });
            }
        }

        // Get current user ID from claims
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                       ?? User.FindFirst("sub")?.Value;
        
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            userId = Guid.Empty; // Fallback if user ID not found
        }

        // Queue command to Table Storage
        var queuedCommand = new CommandQueueDto(
            CommandId: command.CommandId,
            TenantId: tenantId,
            DeviceId: command.DeviceId,
            CommandName: command.CommandName,
            CommandType: command.CommandType,
            CommandData: command.CommandData,
            HttpMethod: command.HttpMethod,
            RequestBody: command.RequestBody,
            RequestHeaders: command.RequestHeaders,
            QueuedUtc: DateTimeOffset.UtcNow,
            QueuedByUserId: userId,
            DeviceIp: device.Ip,
            DevicePort: device.Port,
            DeviceType: device.Type,
            MonitorRecordingStatus: command.MonitorRecordingStatus,
            StatusEndpoint: command.StatusEndpoint,
            StatusPollingIntervalSeconds: command.StatusPollingIntervalSeconds,
            Status: "Pending"
        );

        await _queueStore.EnqueueAsync(queuedCommand, ct);

        return Ok(new { 
            success = true, 
            message = "Command queued for execution",
            commandId = command.CommandId,
            deviceName = device.Name
        });
    }

    // DTOs for request bodies
    public record CreateCommandDto(
        Guid DeviceId,
        string CommandName,
        string? Description,
        string CommandType,
        string? CommandData,
        string? HttpMethod,
        string? RequestBody,
        string? RequestHeaders,
        bool? RequireDeviceOnline,
        bool? MonitorRecordingStatus,
        string? StatusEndpoint,
        int? StatusPollingIntervalSeconds);

    public record UpdateCommandDto(
        string? CommandName,
        string? Description,
        string? CommandType,
        string? CommandData,
        string? HttpMethod,
        string? RequestBody,
        string? RequestHeaders,
        bool? RequireDeviceOnline,
        bool? MonitorRecordingStatus,
        string? StatusEndpoint,
        int? StatusPollingIntervalSeconds);

    // Legacy DeviceAction endpoints for backward compatibility
    [HttpGet("getqueue/{deviceId}")]
    public async Task<IActionResult> GetQueueLegacy(string deviceId)
    {
        return Ok(new List<object>()); // Legacy endpoint - returns empty for now
    }

    [HttpPost("execute/{commandId:guid}")]
    public async Task<IActionResult> ExecuteLegacy(Guid commandId, CancellationToken ct)
    {
        // Redirect to new trigger endpoint
        return await Trigger(commandId, ct);
    }
}
