using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProdControlAV.API.Models;
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
    private readonly IJwtService _jwtService;
    
    public AgentsController(AppDbContext db, IAgentAuth auth, IJwtService jwtService) 
    { 
        _db = db; 
        _auth = auth;
        _jwtService = jwtService;
    }

    public sealed class HeartbeatRequest
    {
        public string AgentKey { get; set; } = string.Empty;
        public string Hostname { get; set; } = string.Empty;
        public string? IpAddress { get; set; }
        public string? Version { get; set; }
    }

    /// <summary>
    /// Authenticate agent and obtain JWT token
    /// </summary>
    /// <param name="request">Agent authentication request with agent key</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>JWT token for subsequent API calls</returns>
    [HttpPost("auth")]
    [ProducesResponseType<AgentAuthResponse>(200)]
    [ProducesResponseType<object>(401)]
    [ProducesResponseType<object>(400)]
    public async Task<IActionResult> Authenticate([FromBody] AgentAuthRequest request, CancellationToken ct)
    {
        // Validate the agent key
        var (agent, err) = await _auth.ValidateAsync(request.AgentKey, ct);
        if (agent is null)
        {
            return Unauthorized(new { error = err });
        }

        // Generate JWT token
        var (token, expiresAt) = _jwtService.GenerateToken(agent);

        return Ok(new AgentAuthResponse
        {
            Token = token,
            ExpiresAt = expiresAt,
            TokenType = "Bearer"
        });
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
    [Authorize(Policy = "JwtAgent")]
    public async Task<ActionResult<List<DeviceTargetDto>>> GetDevices(CancellationToken ct)
    {
        // Extract agent information from JWT claims
        var agentIdClaim = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
        var tenantIdClaim = User.FindFirst("tenantId")?.Value;

        if (!Guid.TryParse(agentIdClaim, out var agentId) || !Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            return Unauthorized(new { error = "invalid_token_claims" });
        }

        // For now, all tenant devices. Later: add per-agent assignment table.
        var devices = await _db.Devices
            .Where(d => d.TenantId == tenantId)
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
        public Guid? TenantId { get; set; }
        public List<StatusReading> Readings { get; set; } = new();
    }

    [HttpPost("status")]
    [Authorize(Policy = "JwtAgent")]
    public async Task<IActionResult> Status([FromBody] StatusUploadRequest req, CancellationToken ct)
    {
        // Extract agent information from JWT claims
        var agentIdClaim = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
        var tenantIdClaim = User.FindFirst("tenantId")?.Value;

        if (!Guid.TryParse(agentIdClaim, out var agentId) || !Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            return Unauthorized(new { error = "invalid_token_claims" });
        }

        // Validate that the request tenant matches the JWT tenant
        if (req.TenantId.HasValue && req.TenantId != tenantId)
        {
            return Forbid();
        }

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

    public sealed class CommandPullRequest { public int Max { get; set; } = 10; }
    public sealed class CommandPullResponse { public List<CommandEnvelope> Commands { get; set; } = new(); }

    [HttpPost("commands/next")]
    [Authorize(Policy = "JwtAgent")]
    public async Task<ActionResult<CommandPullResponse>> Next([FromBody] CommandPullRequest req, CancellationToken ct)
    {
        // Extract agent information from JWT claims
        var agentIdClaim = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
        var tenantIdClaim = User.FindFirst("tenantId")?.Value;

        if (!Guid.TryParse(agentIdClaim, out var agentId) || !Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            return Unauthorized(new { error = "invalid_token_claims" });
        }

        var cmds = await _db.AgentCommands
            .Where(c => c.AgentId == agentId && c.TenantId == tenantId && c.TakenUtc == null)
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

    public sealed class CommandCompleteRequest { public Guid CommandId { get; set; } public bool Success { get; set; } public string? Message { get; set; } public int? DurationMs { get; set; } }

    [HttpPost("commands/complete")]
    [Authorize(Policy = "JwtAgent")]
    public async Task<IActionResult> Complete([FromBody] CommandCompleteRequest req, CancellationToken ct)
    {
        // Extract agent information from JWT claims
        var agentIdClaim = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
        var tenantIdClaim = User.FindFirst("tenantId")?.Value;

        if (!Guid.TryParse(agentIdClaim, out var agentId) || !Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            return Unauthorized(new { error = "invalid_token_claims" });
        }

        var cmd = await _db.AgentCommands.FirstOrDefaultAsync(c => c.Id == req.CommandId && c.AgentId == agentId, ct);
        if (cmd is null) return NotFound(new { error = "command_not_found" });

        cmd.CompletedUtc = DateTime.UtcNow;
        cmd.Success = req.Success;
        cmd.Message = req.Message;
        await _db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }
}
