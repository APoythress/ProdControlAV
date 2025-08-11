using Microsoft.AspNetCore.Mvc;
using ProdControlAV.WebApp.Models;

namespace ProdControlAV.API.Controllers;

[ApiController]
[Route("api/devices")]
public class StatusController : ControllerBase
{
    private readonly IDeviceStatusRepository _repo;

    public StatusController(IDeviceStatusRepository repo)
    {
        _repo = repo;
    }

    // POST api/devices/status
    // Used to populate the status of the clients device
    [HttpPost("status")]
    public async Task<IActionResult> PostStatus([FromBody] DeviceStatusDto dto)
    {
        var log = new DeviceStatusLog
        {
            DeviceName = dto.Name,
            IP = dto.IP,
            IsOnline = dto.IsOnline,
            LastPingMs = dto.LastPingMs,
            Timestamp = DateTime.UtcNow
        };

        await _repo.SaveStatusAsync(log);
        return Ok();
    }
}
