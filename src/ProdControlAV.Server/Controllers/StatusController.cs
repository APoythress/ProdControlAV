using Microsoft.AspNetCore.Mvc;
using ProdControlAV.Core.Models;

namespace ProdControlAV.Server.Controllers;

[ApiController]
[Route("api/status")]
public class StatusController : ControllerBase
{
    private static readonly List<DeviceStatus> Statuses = new();

    [HttpGet]
    public IActionResult GetAll()
    {
        return Ok(Statuses);
    }

    [HttpPost]
    public IActionResult ReportStatus(DeviceStatus status)
    {
        var existing = Statuses.FirstOrDefault(s => s.DeviceId == status.DeviceId);
        if (existing != null)
            Statuses.Remove(existing);

        Statuses.Add(status);
        return Ok();
    }
}
