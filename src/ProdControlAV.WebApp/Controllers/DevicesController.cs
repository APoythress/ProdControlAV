using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using ProdControlAV.Infrastructure.Services;
using ProdControlAV.WebApp.Models;

namespace ProdControlAV.Server.Controllers
{
    [ApiController]
    [Route("api/devices")]
    public class DevicesController : ControllerBase
    {
        private readonly DeviceManager _deviceManager;

        public DevicesController(DeviceManager deviceManager)
        {
            _deviceManager = deviceManager;
        }

        // GET api/devices
        // Used to get all devices from the server to populate dashboard
        [HttpGet]
        public ActionResult<List<DeviceStatusDto>> GetAll()
        {
            return Ok(_deviceManager.GetAllDevices());
        }

        // GET api/devices/ping
        // Used to ping the device on a configured basis to update the dashboard with status
        [HttpGet("ping")]
        public async Task<ActionResult<long>> Ping([FromQuery] string ip)
        {
            var result = await _deviceManager.PingDeviceAsync(ip);
            return Ok(result);
        }
        
        // POST api/devices/command
        // Used to send commands to the device
        [HttpPost("command")]
        public async Task<IActionResult> SendCommand([FromBody] CommandRequest request)
        {
            await _deviceManager.SendCommandAsync(request.ip, request.command);
            return Ok();
        }

        public record CommandRequest(string ip, string command);
    }
}