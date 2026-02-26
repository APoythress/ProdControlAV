using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProdControlAV.Core.Models;
using ProdControlAV.Infrastructure.Services;

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
    // Reads from Azure Table Storage for status and polling info, and enriches with SQL data for ATEM fields
    // Returns a simple DTO compatible with dashboard needs
    [HttpGet("devices")]
    public async Task<ActionResult<List<DashboardDeviceDto>>> Devices(CancellationToken ct)
    {
        var devices = new List<DashboardDeviceDto>();
        
        // Get all device IDs from Table Storage first
        var tableDevices = new List<(Guid Id, string Name, string Model, string Brand, string Type, bool AllowTelNet, string IpAddress, int Port, string Location, Guid TenantId, string Status, DateTimeOffset? LastSeenUtc, DateTimeOffset? LastPolledUtc)>();
        await foreach (var device in _deviceStore.GetAllForTenantAsync(_tenant.TenantId, ct))
        {
            tableDevices.Add((device.Id, device.Name, device.Model ?? "", device.Brand ?? "", device.Type, device.AllowTelNet, device.IpAddress, device.Port, device.Location, device.TenantId, device.Status, device.LastSeenUtc, device.LastPolledUtc));
        }
        
        // Fetch ATEM fields from SQL for all devices
        var deviceIds = tableDevices.Select(d => d.Id).ToList();
        var sqlDevices = await _db.Devices
            .Where(d => deviceIds.Contains(d.Id) && d.TenantId == _tenant.TenantId)
            .Select(d => new { d.Id, d.AtemEnabled, d.AtemTransitionDefaultRate, d.AtemTransitionDefaultType })
            .ToListAsync(ct);
        
        var atemFieldsDict = sqlDevices.ToDictionary(d => d.Id);
        
        // Combine data from both sources
        foreach (var device in tableDevices)
        {
            var atemFields = atemFieldsDict.GetValueOrDefault(device.Id);
            devices.Add(new DashboardDeviceDto
            {
                Id = device.Id,
                Name = device.Name,
                Model = device.Model,
                Brand = device.Brand,
                Type = device.Type,
                AllowTelNet = device.AllowTelNet,
                Ip = device.IpAddress,
                Port = device.Port,
                Location = device.Location,
                TenantId = device.TenantId,
                Status = string.Equals(device.Status, "ONLINE", StringComparison.OrdinalIgnoreCase), // Case-insensitive check for online status
                LastSeenUtc = device.LastSeenUtc,
                LastPolledUtc = device.LastPolledUtc,
                AtemEnabled = atemFields?.AtemEnabled,
                AtemTransitionDefaultRate = atemFields?.AtemTransitionDefaultRate,
                AtemTransitionDefaultType = atemFields?.AtemTransitionDefaultType
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
        public bool? AtemEnabled { get; init; } // Enable ATEM control for this device
        public int? AtemTransitionDefaultRate { get; init; } // Default transition rate in frames
        public string? AtemTransitionDefaultType { get; init; } // Default transition type: "mix" or "cut"
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

    public record UpsertDevice(Guid? Id, string Name, string Model, string Brand, string Type, bool AllowTelNet, string Ip, int? Port, string? Location, int? PingFrequencySeconds, bool? AtemEnabled, int? AtemTransitionDefaultRate, string? AtemTransitionDefaultType, bool? SmsAlertsEnabled);

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
            PingFrequencySeconds = dto.PingFrequencySeconds.GetValueOrDefault(300),
            SmsAlertsEnabled = dto.SmsAlertsEnabled.GetValueOrDefault(true), // Default to enabled
            AtemEnabled = dto.AtemEnabled,
            AtemTransitionDefaultRate = dto.AtemTransitionDefaultRate,
            AtemTransitionDefaultType = dto.AtemTransitionDefaultType?.Trim()
        };
        _db.Devices.Add(d);
        await _db.SaveChangesAsync();
        
        // Write directly to Table Storage (no longer using outbox pattern)
        try
        {
            await _deviceStore.UpsertAsync(
                _tenant.TenantId,
                d.Id,
                d.Name,
                d.Ip,
                d.Type,
                DateTimeOffset.UtcNow,
                d.Model,
                d.Brand,
                d.Location,
                d.AllowTelNet,
                d.Port,
                d.SmsAlertsEnabled,
                CancellationToken.None);
            _logger.LogInformation("Created device {DeviceId} in SQL and Table Storage", d.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write device {DeviceId} to Table Storage, but SQL write succeeded", d.Id);
        }
        
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
        if (dto.SmsAlertsEnabled.HasValue)
            d.SmsAlertsEnabled = dto.SmsAlertsEnabled.Value;
        d.AllowTelNet = dto.AllowTelNet;
        d.Location = dto.Location?.Trim();
        d.AtemEnabled = dto.AtemEnabled;
        d.AtemTransitionDefaultRate = dto.AtemTransitionDefaultRate;
        d.AtemTransitionDefaultType = dto.AtemTransitionDefaultType?.Trim();

        await _db.SaveChangesAsync();
        
        // Write directly to Table Storage (no longer using outbox pattern)
        try
        {
            await _deviceStore.UpsertAsync(
                _tenant.TenantId,
                d.Id,
                d.Name,
                d.Ip,
                d.Type,
                DateTimeOffset.UtcNow,
                d.Model,
                d.Brand,
                d.Location,
                d.AllowTelNet,
                d.Port,
                d.SmsAlertsEnabled,
                CancellationToken.None);
            _logger.LogInformation("Updated device {DeviceId} in SQL and Table Storage", d.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update device {DeviceId} in Table Storage, but SQL update succeeded", d.Id);
        }
        
        return Ok(d);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var d = await _db.Devices.FindAsync(id);
        if (d is null || d.TenantId != _tenant.TenantId) return NotFound();
        _db.Devices.Remove(d);
        await _db.SaveChangesAsync();
        
        // Delete from Table Storage (no longer using outbox pattern)
        try
        {
            await _deviceStore.DeleteAsync(_tenant.TenantId, id, CancellationToken.None);
            _logger.LogInformation("Deleted device {DeviceId} from SQL and Table Storage", id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete device {DeviceId} from Table Storage, but SQL delete succeeded", id);
        }
        
        return NoContent();
    }
}
