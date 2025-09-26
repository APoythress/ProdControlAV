using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProdControlAV.Core.Models;

[ApiController]
[Authorize(Policy = "IsMember")]
[Route("api/[controller]")]
public class DevicesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantProvider _tenant;
    public DevicesController(AppDbContext db, ITenantProvider tenant) { _db = db; _tenant = tenant; }
    
    // GET api/devices/devices
    [HttpGet("devices")]
    public Task<List<Device>> Devices(CancellationToken ct)
    {
        var devices = _db.Devices
            .AsNoTracking()
            .Where(dv => dv.TenantId == _tenant.TenantId)
            .OrderBy(dv => dv.Name)
            .ToListAsync(ct);
        return devices;
    }

    // GET api/devices/actions
    [HttpGet("actions")]
    public async Task<ActionResult<IEnumerable<DeviceAction>>> Actions()
    {
        var tenantId = _tenant.TenantId;
        var items = await _db.DeviceActions
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .OrderBy(a => a.ActionName)
            .ToListAsync();
        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Device>> Get(Guid id) // use Guid to match route
        => await _db.Devices.FindAsync(id, _tenant.TenantId) is { } d ? Ok(d) : NotFound();

    public record UpsertDevice(Guid? Id, string Name, string Model, string Brand, string Type, bool AllowTelNet, string Ip, int? Port);

    [HttpPost]
    [Authorize(Policy = "IsMember")]
    public async Task<ActionResult<Device>> Create([FromBody] UpsertDevice dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name) || string.IsNullOrWhiteSpace(dto.Ip))
            return BadRequest(new { error = "name and ip are required" });

        var d = new Device
        {
            Id = dto.Id ?? Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            Name = dto.Name.Trim(),
            Model = dto.Model.Trim(),
            Brand = dto.Brand.Trim(),
            Type = dto.Type.Trim(),
            AllowTelNet = dto.AllowTelNet,
            Ip = dto.Ip.Trim(),
            Port = dto.Port.GetValueOrDefault(80),
            Status = false
        };
        _db.Devices.Add(d);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = d.Id }, d);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<Device>> Update(Guid id, [FromBody] UpsertDevice dto)
    {
        var d = await _db.Devices.FindAsync(id, _tenant.TenantId);
        if (d is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(dto.Name)) d.Name = dto.Name.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Ip))   d.Ip   = dto.Ip.Trim();
        if (dto.Port.HasValue && dto.Port.Value > 0) d.Port = dto.Port.Value;
        d.AllowTelNet = dto.AllowTelNet;

        await _db.SaveChangesAsync();
        return Ok(d);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var d = await _db.Devices.FindAsync(id, _tenant.TenantId);
        if (d is null) return NotFound();
        _db.Devices.Remove(d);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
