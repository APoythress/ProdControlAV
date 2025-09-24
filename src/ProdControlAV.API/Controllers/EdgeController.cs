using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProdControlAV.Core.Models;

[ApiController]
[Route("api/edge")]
[Authorize(Policy = "TenantMember")]
public class EdgeController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantProvider _tenant;
    private readonly IConfiguration _cfg;
    public EdgeController(AppDbContext db, ITenantProvider tenant, IConfiguration cfg) { _db = db; _tenant = tenant; _cfg = cfg; }

    private bool Authorized(HttpRequest req)
    {
        var expected = _cfg["Edge:Key"];
        return string.IsNullOrEmpty(expected) || (req.Headers.TryGetValue("X-Edge-Key", out var v) && v == expected);
    }

    public record HeartbeatDto(Guid Id, string? Name, string? Ip, int? Port, bool Status);

    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat([FromBody] HeartbeatDto hb)
    {
        if (!Authorized(Request)) return Unauthorized();

        var d = await _db.Devices.FirstOrDefaultAsync(x => x.Id == hb.Id && x.TenantId == _tenant.TenantId);
        if (d is null)
        {
            d = new Device
            {
                Id = hb.Id, TenantId = _tenant.TenantId,
                Name = hb.Name
            };
            _db.Devices.Add(d);
        }
        
        if (!string.IsNullOrWhiteSpace(hb.Name)) d.Name = hb.Name!;
        if (!string.IsNullOrWhiteSpace(hb.Ip))   d.Ip   = hb.Ip!;
        
        if (hb.Port.HasValue) d.Port = hb.Port.Value;

        d.Status = hb.Status;
        d.LastChecked = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpGet("devices")]
    public async Task<IActionResult> EdgeDevices()
    {
        if (!Authorized(Request)) return Unauthorized();
        var list = await _db.Devices.AsNoTracking().Select(x => new { id = x.Id, name = x.Name, ip = x.Ip, port = x.Port }).ToListAsync();
        return Ok(list);
    }
}
