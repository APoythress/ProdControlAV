using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProdControlAV.Core.Models;
using ProdControlAV.Infrastructure.Services;

namespace ProdControlAV.API.Controllers;

[ApiController]
[Route("api/command-templates")]
[Authorize(Policy = "TenantMember")]
public class CommandTemplateController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantProvider _tenant;
    private readonly IDeviceActionStore _deviceActionStore;
    private readonly ILogger<CommandTemplateController> _logger;

    public CommandTemplateController(
        AppDbContext db, 
        ITenantProvider tenant,
        IDeviceActionStore deviceActionStore,
        ILogger<CommandTemplateController> logger)
    {
        _db = db;
        _tenant = tenant;
        _deviceActionStore = deviceActionStore;
        _logger = logger;
    }

    /// <summary>
    /// Get all active command templates, optionally filtered by device type
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<CommandTemplate>>> GetTemplates(
        [FromQuery] string? deviceType = null,
        CancellationToken ct = default)
    {
        var query = _db.CommandTemplates.Where(t => t.IsActive);

        if (!string.IsNullOrWhiteSpace(deviceType))
        {
            query = query.Where(t => t.DeviceType == deviceType);
        }

        var templates = await query
            .OrderBy(t => t.DeviceType)
            .ThenBy(t => t.Category)
            .ThenBy(t => t.DisplayOrder)
            .ToListAsync(ct);

        return Ok(templates);
    }

    /// <summary>
    /// Get a specific command template by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<CommandTemplate>> GetTemplate(Guid id, CancellationToken ct = default)
    {
        var template = await _db.CommandTemplates.FindAsync(new object[] { id }, ct);
        
        if (template == null)
            return NotFound(new { error = "Command template not found" });

        return Ok(template);
    }

    /// <summary>
    /// Create a DeviceAction from a command template
    /// </summary>
    [HttpPost("{templateId}/apply")]
    [Authorize(Policy = "TenantMember")]
    public async Task<ActionResult<DeviceAction>> ApplyTemplate(
        Guid templateId,
        [FromBody] ApplyTemplateRequest request,
        CancellationToken ct = default)
    {
        // Validate tenant context
        var tenantId = _tenant.TenantId;
        if (tenantId == Guid.Empty)
            return Unauthorized(new { error = "missing_or_invalid_tenant" });

        // Get the template
        var template = await _db.CommandTemplates.FindAsync(new object[] { templateId }, ct);
        if (template == null)
            return NotFound(new { error = "Command template not found" });

        if (!template.IsActive)
            return BadRequest(new { error = "Command template is not active" });

        // Verify the device exists and belongs to the tenant
        var device = await _db.Devices.FindAsync(new object[] { request.DeviceId }, ct);
        if (device == null)
            return NotFound(new { error = "Device not found" });

        // Create the DeviceAction from the template
        var deviceAction = new DeviceAction
        {
            ActionId = Guid.NewGuid(),
            DeviceId = request.DeviceId,
            TenantId = tenantId,
            ActionName = string.IsNullOrWhiteSpace(request.CustomName) 
                ? template.Name 
                : request.CustomName,
            Command = template.Endpoint,
            HttpMethod = template.HttpMethod
        };

        _db.DeviceActions.Add(deviceAction);
        await _db.SaveChangesAsync(ct);

        // Write directly to Table Storage (no longer using outbox pattern)
        try
        {
            await _deviceActionStore.UpsertAsync(
                tenantId,
                deviceAction.ActionId,
                deviceAction.DeviceId,
                deviceAction.ActionName,
                ct);
            _logger.LogInformation("Created device action {ActionId} in SQL and Table Storage", deviceAction.ActionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write device action {ActionId} to Table Storage, but SQL write succeeded", deviceAction.ActionId);
        }

        return CreatedAtAction(
            nameof(GetTemplate),
            new { id = templateId },
            deviceAction);
    }

    public record ApplyTemplateRequest(Guid DeviceId, string? CustomName = null);
}
