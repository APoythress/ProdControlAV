using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProdControlAV.Core.Models;
using ProdControlAV.Infrastructure.Services;
using System.Text.Json;

[ApiController]
[Authorize(Policy = "TenantMember")]
[Route("api/[controller]")]
public class DevicesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantProvider _tenant;
    private readonly IDeviceStore _deviceStore;
    private readonly IDeviceActionStore _deviceActionStore;
    private readonly ILogger<DevicesController> _logger;
    
    public DevicesController(AppDbContext db, ITenantProvider tenant, IDeviceStore deviceStore, 
        IDeviceActionStore deviceActionStore, ILogger<DevicesController> logger) 
    { 
        _db = db; 
        _tenant = tenant; 
        _deviceStore = deviceStore;
        _deviceActionStore = deviceActionStore;
        _logger = logger;
    }
    
    // GET api/devices/devices
    // Now reads from Azure Table Storage instead of SQL
    // Returns a simple DTO compatible with dashboard needs
    [HttpGet("devices")]
    public async Task<ActionResult<List<DashboardDeviceDto>>> Devices(CancellationToken ct)
    {
        var devices = new List<DashboardDeviceDto>();
        await foreach (var device in _deviceStore.GetAllForTenantAsync(_tenant.TenantId, ct))
        {
            devices.Add(new DashboardDeviceDto
            {
                Id = device.Id,
                Name = device.Name,
                Model = device.Model ?? "",
                Brand = device.Brand ?? "",
                Type = device.Type,
                AllowTelNet = device.AllowTelNet,
                Ip = device.IpAddress,
                Port = device.Port,
                Location = device.Location,
                TenantId = device.TenantId,
                Status = string.Equals(device.Status, "ONLINE", StringComparison.OrdinalIgnoreCase), // Case-insensitive check for online status
                LastSeenUtc = device.LastSeenUtc,
                LastPolledUtc = device.LastPolledUtc
            });
        }
        _logger.LogInformation("Retrieved {Count} devices from Table Storage for tenant {TenantId}", devices.Count, _tenant.TenantId);
        return Ok(devices);
    }
    
    // DTO for dashboard - subset of Device model
    public record DashboardDeviceDto
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = "";
        public string Model { get; init; } = "";
        public string Brand { get; init; } = "";
        public string Type { get; init; } = "";
        public bool AllowTelNet { get; init; }
        public string Ip { get; init; } = "";
        public int Port { get; init; }
        public string? Location { get; init; }
        public Guid TenantId { get; init; }
        public bool Status { get; init; } // Online/Offline status for UI
        public DateTimeOffset? LastSeenUtc { get; init; } // When device was last seen
        public DateTimeOffset? LastPolledUtc { get; init; } // When device was last polled
    }

    // GET api/devices/actions
    // Now reads from Azure Table Storage instead of SQL
    [HttpGet("actions")]
    public async Task<ActionResult<List<DashboardDeviceActionDto>>> Actions(CancellationToken ct)
    {
        var actions = new List<DashboardDeviceActionDto>();
        await foreach (var action in _deviceActionStore.GetAllForTenantAsync(_tenant.TenantId, ct))
        {
            actions.Add(new DashboardDeviceActionDto
            {
                ActionId = action.ActionId,
                DeviceId = action.DeviceId,
                ActionName = action.ActionName,
                TenantId = action.TenantId
            });
        }
        _logger.LogInformation("Retrieved {Count} device actions from Table Storage for tenant {TenantId}", actions.Count, _tenant.TenantId);
        return Ok(actions);
    }
    
    // DTO for dashboard device actions
    public record DashboardDeviceActionDto
    {
        public Guid ActionId { get; init; }
        public Guid DeviceId { get; init; }
        public string ActionName { get; init; } = "";
        public Guid TenantId { get; init; }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Device>> Get(Guid id) // use Guid to match route
    {
        var d = await _db.Devices.FindAsync(id);
        if (d is null || d.TenantId != _tenant.TenantId) return NotFound();
        return Ok(d);
    }

    public record UpsertDevice(Guid? Id, string Name, string Model, string Brand, string Type, bool AllowTelNet, string Ip, int? Port, string? Location, int? PingFrequencySeconds);

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
            Location = dto.Location?.Trim(),
            Status = false,
            PingFrequencySeconds = dto.PingFrequencySeconds.GetValueOrDefault(300)
        };
        _db.Devices.Add(d);
        
        // Create outbox entry for projection to Table Storage
        var outboxEntry = new OutboxEntry
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            EntityType = "Device",
            EntityId = d.Id,
            Operation = "Upsert",
            Payload = JsonSerializer.Serialize(d),
            CreatedUtc = DateTimeOffset.UtcNow,
            RetryCount = 0
        };
        _db.OutboxEntries.Add(outboxEntry);
        
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = d.Id }, d);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<Device>> Update(Guid id, [FromBody] UpsertDevice dto)
    {
        var d = await _db.Devices.FindAsync(id);
        if (d is null || d.TenantId != _tenant.TenantId) return NotFound();

        if (!string.IsNullOrWhiteSpace(dto.Name)) d.Name = dto.Name.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Ip))   d.Ip   = dto.Ip.Trim();
        if (dto.Port.HasValue && dto.Port.Value > 0) d.Port = dto.Port.Value;
        if (dto.PingFrequencySeconds.HasValue && dto.PingFrequencySeconds.Value >= 5) 
            d.PingFrequencySeconds = dto.PingFrequencySeconds.Value;
        d.AllowTelNet = dto.AllowTelNet;
        d.Location = dto.Location?.Trim();

        // Create outbox entry for projection to Table Storage
        var outboxEntry = new OutboxEntry
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            EntityType = "Device",
            EntityId = d.Id,
            Operation = "Upsert",
            Payload = JsonSerializer.Serialize(d),
            CreatedUtc = DateTimeOffset.UtcNow,
            RetryCount = 0
        };
        _db.OutboxEntries.Add(outboxEntry);
        
        await _db.SaveChangesAsync();
        return Ok(d);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var d = await _db.Devices.FindAsync(id);
        if (d is null || d.TenantId != _tenant.TenantId) return NotFound();
        _db.Devices.Remove(d);
        
        // Create outbox entry for deletion from Table Storage
        var outboxEntry = new OutboxEntry
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            EntityType = "Device",
            EntityId = id,
            Operation = "Delete",
            Payload = null,
            CreatedUtc = DateTimeOffset.UtcNow,
            RetryCount = 0
        };
        _db.OutboxEntries.Add(outboxEntry);
        
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
