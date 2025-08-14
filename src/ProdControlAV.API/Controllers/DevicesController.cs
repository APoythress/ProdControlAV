using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProdControlAV.Core.Models;

[ApiController]
[Route("api/devices")]
public class DevicesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantProvider _tenant;
    public DevicesController(AppDbContext db, ITenantProvider tenant) { _db = db; _tenant = tenant; }

    // GET api/devices
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Device>>> Devices()
        => Ok(await _db.Devices.AsNoTracking().ToListAsync());
    
    // GET api/devices/actions
    [HttpGet("actions")]
    public async Task<ActionResult<IEnumerable<DeviceAction>>> Actions()
        => Ok(await _db.DeviceActions.AsNoTracking().OrderBy(d => d.ActionName).ToListAsync());

    [HttpGet("{id}")]
    public async Task<ActionResult<Device>> Get(string id)
        => await _db.Devices.FindAsync(id, _tenant.TenantId) is { } d ? Ok(d) : NotFound();

    public record UpsertDevice(Guid? Id, string Name, string Ip, int? Port);

    [HttpPost]
    public async Task<ActionResult<Device>> Create([FromBody] UpsertDevice dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name) || string.IsNullOrWhiteSpace(dto.Ip))
            return BadRequest(new { error = "name and ip are required" });

        var d = new Device
        {
            Id = dto.Id ?? Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            Name = dto.Name.Trim(),
            Ip = dto.Ip.Trim(),
            Port = dto.Port.GetValueOrDefault(80),
            Status = false
        };
        _db.Devices.Add(d);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = d.Id }, d);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<Device>> Update(string id, [FromBody] UpsertDevice dto)
    {
        var d = await _db.Devices.FindAsync(id, _tenant.TenantId);
        if (d is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(dto.Name)) d.Name = dto.Name.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Ip))   d.Ip   = dto.Ip.Trim();
        if (dto.Port.HasValue && dto.Port.Value > 0) d.Port = dto.Port.Value;

        await _db.SaveChangesAsync();
        return Ok(d);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var d = await _db.Devices.FindAsync(id, _tenant.TenantId);
        if (d is null) return NotFound();
        _db.Devices.Remove(d);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
