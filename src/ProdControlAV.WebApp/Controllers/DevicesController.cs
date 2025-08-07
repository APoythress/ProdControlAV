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

        [HttpGet]
        public ActionResult<List<DeviceStatusDto>> GetAll()
        {
            return Ok(_deviceManager.GetAllDevices());
        }

        [HttpGet("ping")]
        public async Task<ActionResult<long>> Ping([FromQuery] string ip)
        {
            var result = await _deviceManager.PingDeviceAsync(ip);
            return Ok(result);
        }

        [HttpPost("command")]
        public async Task<IActionResult> SendCommand([FromBody] CommandRequest request)
        {
            await _deviceManager.SendCommandAsync(request.ip, request.command);
            return Ok();
        }

        public record CommandRequest(string ip, string command);
    }
}