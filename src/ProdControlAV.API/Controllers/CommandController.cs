using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using ProdControlAV.Core.Interfaces;
using ProdControlAV.Core.Models;

namespace ProdControlAV.API.Controllers;

[ApiController]
[Route("api/commands")]
public class CommandController : ControllerBase
{
    private readonly ICommandQueue _queue;
    private readonly AppDbContext _db;
    private readonly ITenantProvider _tenant;

    public CommandController(ICommandQueue queue, ITenantProvider tenant, AppDbContext db)
    {
        _queue = queue;
        _tenant = tenant;
        _db = db;
    }

    // Get the status
    [Route("api/commands/getqueue")]
    [HttpGet("{deviceId}")]
    public async Task<IActionResult> GetCommands(string deviceId)
    {
        var commands = await _queue.FetchPendingCommandsAsync(deviceId);
        return Ok(commands);
    }
    
    [HttpGet("{deviceId}")]
    
    public async Task<ActionResult<DeviceAction>> Get(string id)
        => await _db.DeviceActions.FindAsync(id, id) is { } d ? Ok(d) : NotFound();
    
    public record UpsertCommand(Guid DeviceId, string ActionName, string Command);

    [HttpPost]
    public async Task<ActionResult<DeviceAction>> Create([FromBody] UpsertCommand dto)
    {
        if (dto.DeviceId == Guid.Empty || string.IsNullOrWhiteSpace(dto.Command))
            return BadRequest(new { error = "deviceId and command are required" });

        var d = new DeviceAction
        {
            DeviceId = dto.DeviceId,
            TenantId = _tenant.TenantId,
            ActionName = dto.ActionName.Trim(),
            Command = dto.Command.Trim(),
        };
        _db.DeviceActions.Add(d);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetCommands), new { id = d.DeviceId }, d);
    }
}
