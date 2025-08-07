using Microsoft.AspNetCore.Mvc;
using ProdControlAV.Core.Interfaces;

namespace ProdControlAV.Server.Controllers;

[ApiController]
[Route("api/commands")]
public class CommandController : ControllerBase
{
    private readonly ICommandQueue _queue;

    public CommandController(ICommandQueue queue)
    {
        _queue = queue;
    }

    [HttpGet("{deviceId}")]
    public async Task<IActionResult> GetCommands(string deviceId)
    {
        var commands = await _queue.FetchPendingCommandsAsync(deviceId);
        return Ok(commands);
    }
}
