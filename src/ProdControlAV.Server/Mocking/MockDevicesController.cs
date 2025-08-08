using Microsoft.AspNetCore.Mvc;

namespace ProdControlAV.Server.Mocking.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MockDevicesController : ControllerBase
{
    [HttpGet("status")]
    public IActionResult GetMockDeviceStatus()
    {
        var devices = new[]
        {
            new { Name = "BlackMagic Design ATEM TV Studio", IP = "192.168.1.100", Status = "ONLINE" },
            new { Name = "Behringer WING", IP = "192.168.1.247", Status = "ONLINE" },
            new { Name = "PA AMP", IP = "192.168.1.206", Status = "OFFLINE" }
        };

        return Ok(devices);
    }
}