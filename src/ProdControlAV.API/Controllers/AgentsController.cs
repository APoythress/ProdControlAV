using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProdControlAV.API.Models;
using ProdControlAV.API.Services;
using ProdControlAV.Core.Models;
using static Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults;

namespace ProdControlAV.API.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/agents")]
public sealed class AgentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAgentAuth _auth;
    private readonly IJwtService _jwtService;
    private readonly ILogger<AgentsController> _logger;
    
    public AgentsController(AppDbContext db, IAgentAuth auth, IJwtService jwtService, ILogger<AgentsController> logger) 
    { 
        _db = db; 
        _auth = auth;
        _jwtService = jwtService;
        _logger = logger;
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
        _logger.LogInformation("[AUTH] Received agent auth request: AgentKey={AgentKey}, Host={Host}", request.AgentKey, HttpContext.Connection.RemoteIpAddress);
        // Validate the agent key
        var (agent, err) = await _auth.ValidateAsync(request.AgentKey, ct);
        if (agent is null)
        {
            _logger.LogWarning("[AUTH] Agent key validation failed: {Error}", err);
            return Unauthorized(new { error = err });
        }
        // Generate JWT token
        var (token, expiresAt) = _jwtService.GenerateToken(agent);
        _logger.LogInformation("[AUTH] JWT issued for AgentId={AgentId}, TenantId={TenantId}, ExpiresAt={ExpiresAt}", agent.Id, agent.TenantId, expiresAt);
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
        _logger.LogInformation("[HEARTBEAT] Received heartbeat: AgentKey={AgentKey}, Hostname={Hostname}, IpAddress={IpAddress}, Version={Version}", req.AgentKey, req.Hostname, req.IpAddress, req.Version);
        var (agent, err) = await _auth.ValidateAsync(req.AgentKey, ct);
        if (agent is null) {
            _logger.LogWarning("[HEARTBEAT] Agent key validation failed: {Error}", err);
            return Unauthorized(new { error = err });
        }
        agent.LastHostname = req.Hostname;
        agent.LastIp = req.IpAddress;
        agent.Version = req.Version;
        agent.LastSeenUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("[HEARTBEAT] Updated agent status: AgentId={AgentId}, LastIp={LastIp}, Version={Version}", agent.Id, agent.LastIp, agent.Version);
        return Ok(new { ok = true });
    }

    [HttpGet("devices")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<List<DeviceTargetDto>>> GetDevices(CancellationToken ct)
    {
        _logger.LogInformation("[DEVICES] Headers: {Headers}", HttpContext.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString()));
        var agentIdClaim = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
        var tenantIdClaim = User.Claims.FirstOrDefault(c => c.Type == "tenantId" || c.Type.EndsWith("/tenantId"))?.Value;
        _logger.LogInformation("[DEVICES] Extracted claims: sub={Sub}, tenantId={TenantId}", agentIdClaim, tenantIdClaim);
        if (!Guid.TryParse(agentIdClaim, out var agentId) || !Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            _logger.LogWarning("[DEVICES] Invalid token claims: sub={Sub}, tenantId={TenantId}", agentIdClaim, tenantIdClaim);
            return Unauthorized(new { error = "invalid_token_claims" });
        }
        var devices = await _db.Devices
            .Where(d => d.TenantId == tenantId)
            .Select(d => new DeviceTargetDto
            {
                Id = d.Id,
                IpAddress = d.Ip,
                Type = d.Type,
                TcpPort = null
            })
            .ToListAsync(ct);
        _logger.LogInformation("[DEVICES] Returning {Count} devices for TenantId={TenantId}", devices.Count, tenantId);
        return Ok(devices);
    }

    public sealed class StatusUploadRequest
    {
        public Guid? TenantId { get; set; }
        public List<StatusReading> Readings { get; set; } = new();
    }

    [HttpPost("status")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Status([FromBody] StatusUploadRequest req, CancellationToken ct)
    {
        _logger.LogInformation("[STATUS] Headers: {Headers}", HttpContext.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString()));
        _logger.LogInformation("[STATUS] Body: {Body}", req);
        var agentIdClaim = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
        var tenantIdClaim = User.Claims.FirstOrDefault(c => c.Type == "tenantId" || c.Type.EndsWith("/tenantId"))?.Value;
        _logger.LogInformation("[STATUS] Extracted claims: sub={Sub}, tenantId={TenantId}", agentIdClaim, tenantIdClaim);
        if (!Guid.TryParse(agentIdClaim, out var agentId) || !Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            _logger.LogWarning("[STATUS] Invalid token claims: sub={Sub}, tenantId={TenantId}", agentIdClaim, tenantIdClaim);
            return Unauthorized(new { error = "invalid_token_claims" });
        }
        if (req.TenantId.HasValue && req.TenantId != tenantId)
        {
            _logger.LogWarning("[STATUS] Request tenantId does not match JWT tenantId: Request={RequestTenantId}, JWT={JwtTenantId}", req.TenantId, tenantId);
            return Forbid();
        }
        var now = DateTime.UtcNow;
        foreach (var r in req.Readings)
        {
            _db.DeviceStatusLogs.Add(new DeviceStatusLog
            {
                Id = Guid.NewGuid(),
                IsOnline = r.IsOnline ? true : false,
            });
        }
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("[STATUS] Saved {Count} status readings for TenantId={TenantId}", req.Readings.Count, tenantId);
        return Ok(new { ok = true });
    }

    public sealed class CommandPullRequest { public int Max { get; set; } = 10; }
    public sealed class CommandPullResponse { public List<CommandEnvelope> Commands { get; set; } = new(); }

    [HttpPost("commands/next")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<CommandPullResponse>> Next([FromBody] CommandPullRequest req, CancellationToken ct)
    {
        _logger.LogInformation("[COMMANDS/NEXT] Headers: {Headers}", HttpContext.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString()));
        _logger.LogInformation("[COMMANDS/NEXT] Body: {Body}", req);
        var agentIdClaim = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
        var tenantIdClaim = User.Claims.FirstOrDefault(c => c.Type == "tenantId" || c.Type.EndsWith("/tenantId"))?.Value;
        _logger.LogInformation("[COMMANDS/NEXT] Extracted claims: sub={Sub}, tenantId={TenantId}", agentIdClaim, tenantIdClaim);
        if (!Guid.TryParse(agentIdClaim, out var agentId) || !Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            _logger.LogWarning("[COMMANDS/NEXT] Invalid token claims: sub={Sub}, tenantId={TenantId}", agentIdClaim, tenantIdClaim);
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
        _logger.LogInformation("[COMMANDS/NEXT] Returning {Count} commands for AgentId={AgentId}, TenantId={TenantId}", cmds.Count, agentId, tenantId);
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
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Complete([FromBody] CommandCompleteRequest req, CancellationToken ct)
    {
        _logger.LogInformation("[COMMANDS/COMPLETE] Headers: {Headers}", HttpContext.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString()));
        _logger.LogInformation("[COMMANDS/COMPLETE] Body: {Body}", req);
        var agentIdClaim = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
        var tenantIdClaim = User.Claims.FirstOrDefault(c => c.Type == "tenantId" || c.Type.EndsWith("/tenantId"))?.Value;
        _logger.LogInformation("[COMMANDS/COMPLETE] Extracted claims: sub={Sub}, tenantId={TenantId}", agentIdClaim, tenantIdClaim);
        if (!Guid.TryParse(agentIdClaim, out var agentId) || !Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            _logger.LogWarning("[COMMANDS/COMPLETE] Invalid token claims: sub={Sub}, tenantId={TenantId}", agentIdClaim, tenantIdClaim);
            return Unauthorized(new { error = "invalid_token_claims" });
        }
        var cmd = await _db.AgentCommands.FirstOrDefaultAsync(c => c.Id == req.CommandId && c.AgentId == agentId, ct);
        if (cmd is null) {
            _logger.LogWarning("[COMMANDS/COMPLETE] Command not found: CommandId={CommandId}, AgentId={AgentId}", req.CommandId, agentId);
            return NotFound(new { error = "command_not_found" });
        }
        cmd.CompletedUtc = DateTime.UtcNow;
        cmd.Success = req.Success;
        cmd.Message = req.Message;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("[COMMANDS/COMPLETE] Command completed: CommandId={CommandId}, Success={Success}, AgentId={AgentId}", req.CommandId, req.Success, agentId);
        return Ok(new { ok = true });
    }
}
