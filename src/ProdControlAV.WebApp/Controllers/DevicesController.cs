using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using ProdControlAV.Core.Models;
using ProdControlAV.Infrastructure.Services;
using ProdControlAV.WebApp.Models;

namespace ProdControlAV.Server.Controllers
{
    [ApiController]
    [Route("api/devices")]
    public class DevicesController : ControllerBase
    {
        private readonly DeviceApiClient _deviceApiClient;

        public DevicesController(DeviceApiClient deviceManager)
        {
            _deviceApiClient = deviceManager;
        }

        // GET api/devices
        // Used to get all devices from the server to populate dashboard
        [HttpGet]
        public ActionResult<List<DeviceStatusDto>> GetAll()
        {
            return Ok(_deviceApiClient.GetDevicesAsync());
        }
        
        [HttpPost]
        [ValidateAntiForgeryToken] // requires the header we set
        public IActionResult Add([FromBody] DeviceModel device)
        {
            var message = _deviceApiClient.AddNewDeviceAsync(device).Result;
            return message == "Success" ? StatusCode(201) : BadRequest(message);
        }

        // GET api/devices/ping
        // Used to ping the device on a configured basis to update the dashboard with status
        [HttpGet("ping")]
        public async Task<ActionResult<long>> Ping([FromQuery] Guid deviceId)
        {
            var result = await _deviceApiClient.PingDeviceAsync(deviceId);
            return Ok(result);
        }
        
        // POST api/devices/command
        // Used to send commands to the device
        [HttpPost("command")]
        public async Task<IActionResult> SendCommand([FromBody] CommandRequest request)
        {
            await _deviceApiClient.SendCommandAsync(request.deviceId, request.command);
            return Ok();
        }

        public record CommandRequest(Guid deviceId, string command);
    }
}