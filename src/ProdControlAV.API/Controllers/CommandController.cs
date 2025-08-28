using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProdControlAV.API.Services;
using ProdControlAV.Core.Interfaces;
using ProdControlAV.Core.Models;

namespace ProdControlAV.API.Controllers;

[ApiController]
[Route("api/commands")]
[Authorize(Policy = "TenantMember")]
public class CommandController : ControllerBase
{
    private readonly ICommandQueue _queue;
    private readonly AppDbContext _db;
    private readonly ITenantProvider _tenant;
    private readonly IDeviceCommandService _svc;

    public CommandController(ICommandQueue queue, ITenantProvider tenant, AppDbContext db, IDeviceCommandService svc)
    {
        _queue = queue;
        _tenant = tenant;
        _db = db;
        _svc = svc;
    }

    [Route("api/commands/getqueue")]
    [HttpGet("{deviceId}")]
    public async Task<IActionResult> GetCommands(string deviceId)
    {
        var commands = await _queue.FetchPendingCommandsAsync(deviceId);
        return Ok(commands);
    }

    [Authorize(Policy = "TenantMember")]
    [HttpPost("execute/{commandId:guid}")]
    public async Task<IActionResult> Execute(Guid commandId, CancellationToken ct)
    {
        // Always use the validated tenant context
        var tenantId = _tenant.TenantId;
        if (tenantId == Guid.Empty)
            return Unauthorized(new { error = "missing_or_invalid_tenant" });

        var result = await _svc.ExecuteDeviceActionAsync(commandId, tenantId, ct);

        if (!result.Success)
            return StatusCode(result.StatusCode ?? 400, new {
                success = false, message = result.Message, statusCode = result.StatusCode, response = result.ResponseBody
            });

        return Ok(new {
            success = true, message = result.Message, statusCode = result.StatusCode, response = result.ResponseBody
        });
    }

    [HttpGet("{deviceId}")]
    public async Task<ActionResult<DeviceAction>> Get(string id)
        => await _db.DeviceActions.FindAsync(id, id) is { } d ? Ok(d) : NotFound();

    public record UpsertCommand(Guid DeviceId, string ActionName, string Command, string HttpMethod);

    [HttpPost]
    public async Task<ActionResult<DeviceAction>> Create([FromBody] UpsertCommand dto, CancellationToken ct)
    {
        if (dto.DeviceId == Guid.Empty || string.IsNullOrWhiteSpace(dto.Command))
            return BadRequest(new { error = "deviceId and command are required" });

        var d = new DeviceAction
        {
            ActionId   = Guid.NewGuid(),
            DeviceId   = dto.DeviceId,
            TenantId   = _tenant.TenantId,
            ActionName = dto.ActionName.Trim(),
            Command    = dto.Command.Trim(),
            HttpMethod = string.IsNullOrWhiteSpace(dto.HttpMethod) ? "POST" : dto.HttpMethod.Trim()
        };
        _db.DeviceActions.Add(d);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetCommands), new { id = d.DeviceId }, d);
    }
}
