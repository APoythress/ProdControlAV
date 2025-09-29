using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProdControlAV.API.Services;
using ProdControlAV.Core.Models;

namespace ProdControlAV.API.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/agents")]
public sealed class AgentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAgentAuth _auth;
    public AgentsController(AppDbContext db, IAgentAuth auth) { _db = db; _auth = auth; }

    public sealed class HeartbeatRequest
    {
        public string AgentKey { get; set; } = string.Empty;
        public string Hostname { get; set; } = string.Empty;
        public string? IpAddress { get; set; }
        public string? Version { get; set; }
    }

    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat([FromBody] HeartbeatRequest req, CancellationToken ct)
    {
        var (agent, err) = await _auth.ValidateAsync(req.AgentKey, ct);
        if (agent is null) return Unauthorized(new { error = err });

        agent.LastHostname = req.Hostname;
        agent.LastIp = req.IpAddress;
        agent.Version = req.Version;
        agent.LastSeenUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(new { ok = true });
    }

    [HttpGet("devices")]
    public async Task<ActionResult<List<DeviceTargetDto>>> GetDevices([FromQuery] string agentKey, CancellationToken ct)
    {
        var (agent, err) = await _auth.ValidateAsync(agentKey, ct);
        if (agent is null) return Unauthorized(new { error = err });

        // For now, all tenant devices. Later: add per-agent assignment table.
        var devices = await _db.Devices
            .Where(d => d.TenantId == agent.TenantId)
            .Select(d => new DeviceTargetDto
            {
                Id = d.Id,
                IpAddress = d.Ip,
                Type = d.Type,
                TcpPort = null // map default ports by Type if desired
            })
            .ToListAsync(ct);

        return Ok(devices);
    }

    public sealed class StatusUploadRequest
    {
        public string AgentKey { get; set; } = string.Empty;
        public Guid? TenantId { get; set; }
        public List<StatusReading> Readings { get; set; } = new();
    }

    [HttpPost("status")]
    public async Task<IActionResult> Status([FromBody] StatusUploadRequest req, CancellationToken ct)
    {
        var (agent, err) = await _auth.ValidateAsync(req.AgentKey, ct);
        if (agent is null) return Unauthorized(new { error = err });
        if (agent.TenantId != req.TenantId) return Forbid();

        var now = DateTime.UtcNow;
        foreach (var r in req.Readings)
        {
            _db.DeviceStatusLogs.Add(new DeviceStatusLog
            {
                Id = Guid.NewGuid(),
                IsOnline = r.IsOnline ? true : false,
                // LatencyMs = r.LatencyMs,
                // RecordedAtUtc = now,
                // Message = r.Message
            });
        }
        await _db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }

    public sealed class CommandPullRequest { public string AgentKey { get; set; } = string.Empty; public int Max { get; set; } = 10; }
    public sealed class CommandPullResponse { public List<CommandEnvelope> Commands { get; set; } = new(); }

    [HttpPost("commands/next")]
    public async Task<ActionResult<CommandPullResponse>> Next([FromBody] CommandPullRequest req, CancellationToken ct)
    {
        var (agent, err) = await _auth.ValidateAsync(req.AgentKey, ct);
        if (agent is null) return Unauthorized(new { error = err });

        var cmds = await _db.AgentCommands
            .Where(c => c.AgentId == agent.Id && c.TenantId == agent.TenantId && c.TakenUtc == null)
            .OrderBy(c => c.CreatedUtc)
            .Take(Math.Clamp(req.Max, 1, 50))
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        cmds.ForEach(c => c.TakenUtc = now);
        await _db.SaveChangesAsync(ct);

        return Ok(new CommandPullResponse
        {
            Commands = cmds.Select(c => new CommandEnvelope
            {
                CommandId = c.Id,
                DeviceId = c.DeviceId,
                Verb = c.Verb,
                Payload = c.Payload
            }).ToList()
        });
    }

    public sealed class CommandCompleteRequest { public string AgentKey { get; set; } = string.Empty; public Guid CommandId { get; set; } public bool Success { get; set; } public string? Message { get; set; } public int? DurationMs { get; set; } }

    [HttpPost("commands/complete")]
    public async Task<IActionResult> Complete([FromBody] CommandCompleteRequest req, CancellationToken ct)
    {
        var (agent, err) = await _auth.ValidateAsync(req.AgentKey, ct);
        if (agent is null) return Unauthorized(new { error = err });

        var cmd = await _db.AgentCommands.FirstOrDefaultAsync(c => c.Id == req.CommandId && c.AgentId == agent.Id, ct);
        if (cmd is null) return NotFound(new { error = "command_not_found" });

        cmd.CompletedUtc = DateTime.UtcNow;
        cmd.Success = req.Success;
        cmd.Message = req.Message;
        await _db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }
}
