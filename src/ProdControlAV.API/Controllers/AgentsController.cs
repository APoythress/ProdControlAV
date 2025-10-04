using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProdControlAV.API.Models;
using ProdControlAV.API.Services;
using ProdControlAV.Core.Models;
using ProdControlAV.Core.Interfaces;
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
    private readonly IAgentCommandQueueService _queueService;
    
    public AgentsController(
        AppDbContext db, 
        IAgentAuth auth, 
        IJwtService jwtService, 
        ILogger<AgentsController> logger,
        IAgentCommandQueueService queueService) 
    { 
        _db = db; 
        _auth = auth;
        _jwtService = jwtService;
        _logger = logger;
        _queueService = queueService;
    }

    private string? GetAgentIdFromClaims()
    {
        // Try short-form JWT claim name first (after NameClaimType mapping)
        var agentId = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
        if (!string.IsNullOrEmpty(agentId))
            return agentId;
        
        // Fallback to long-form claim name (in case mapping still occurs)
        agentId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(agentId))
            return agentId;
        
        // Final fallback - search by claim type containing "nameidentifier"
        return User.Claims.FirstOrDefault(c => c.Type.Contains("nameidentifier", StringComparison.OrdinalIgnoreCase))?.Value;
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
        var agentIdClaim = GetAgentIdFromClaims();
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
                TcpPort = null,
                PingFrequencySeconds = d.PingFrequencySeconds
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
        var agentIdClaim = GetAgentIdFromClaims();
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
        var agentIdClaim = GetAgentIdFromClaims();
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
        var agentIdClaim = GetAgentIdFromClaims();
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
    
    public sealed class CreateCommandRequest 
    { 
        public Guid AgentId { get; set; } 
        public Guid DeviceId { get; set; } 
        public string Verb { get; set; } = string.Empty; 
        public string? Payload { get; set; }
        public DateTime? DueUtc { get; set; }
    }

    /// <summary>
    /// Create a new command for an agent - stores in SQL and enqueues to Azure Queue Storage
    /// </summary>
    [HttpPost("commands/create")]
    [Authorize(Policy = "TenantMember")]
    public async Task<IActionResult> CreateCommand([FromBody] CreateCommandRequest req, CancellationToken ct)
    {
        _logger.LogInformation("[COMMANDS/CREATE] Creating command for AgentId={AgentId}, DeviceId={DeviceId}, Verb={Verb}", 
            req.AgentId, req.DeviceId, req.Verb);
        
        // Get tenant from authenticated user
        var tenantIdClaim = User.Claims.FirstOrDefault(c => c.Type == "tenant_id")?.Value;
        if (!Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            _logger.LogWarning("[COMMANDS/CREATE] Invalid tenant claim");
            return Unauthorized(new { error = "invalid_tenant" });
        }
        
        // Validate agent belongs to tenant
        var agent = await _db.Agents.FirstOrDefaultAsync(a => a.Id == req.AgentId && a.TenantId == tenantId, ct);
        if (agent is null)
        {
            _logger.LogWarning("[COMMANDS/CREATE] Agent not found or not in tenant: AgentId={AgentId}, TenantId={TenantId}", 
                req.AgentId, tenantId);
            return NotFound(new { error = "agent_not_found" });
        }
        
        // Validate device belongs to tenant
        var device = await _db.Devices.FirstOrDefaultAsync(d => d.Id == req.DeviceId && d.TenantId == tenantId, ct);
        if (device is null)
        {
            _logger.LogWarning("[COMMANDS/CREATE] Device not found or not in tenant: DeviceId={DeviceId}, TenantId={TenantId}", 
                req.DeviceId, tenantId);
            return NotFound(new { error = "device_not_found" });
        }
        
        // Create command in SQL (authoritative record)
        var command = new AgentCommand
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AgentId = req.AgentId,
            DeviceId = req.DeviceId,
            Verb = req.Verb,
            Payload = req.Payload,
            CreatedUtc = DateTime.UtcNow,
            DueUtc = req.DueUtc
        };
        
        _db.AgentCommands.Add(command);
        await _db.SaveChangesAsync(ct);
        
        // Enqueue to Azure Queue Storage for delivery
        try
        {
            await _queueService.EnqueueCommandAsync(command, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[COMMANDS/CREATE] Failed to enqueue command {CommandId}, but SQL record created", command.Id);
            // Command is still in SQL, so we don't fail the request
        }
        
        _logger.LogInformation("[COMMANDS/CREATE] Command created: CommandId={CommandId}, AgentId={AgentId}, DeviceId={DeviceId}", 
            command.Id, req.AgentId, req.DeviceId);
        
        return Ok(new { commandId = command.Id, ok = true });
    }
    
    /// <summary>
    /// Receive next command from Azure Queue Storage (agent polling endpoint)
    /// </summary>
    [HttpPost("commands/receive")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> ReceiveCommand(CancellationToken ct)
    {
        var agentIdClaim = GetAgentIdFromClaims();
        var tenantIdClaim = User.Claims.FirstOrDefault(c => c.Type == "tenantId" || c.Type.EndsWith("/tenantId"))?.Value;
        
        if (!Guid.TryParse(agentIdClaim, out var agentId) || !Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            _logger.LogWarning("[COMMANDS/RECEIVE] Invalid token claims: sub={Sub}, tenantId={TenantId}", agentIdClaim, tenantIdClaim);
            return Unauthorized(new { error = "invalid_token_claims" });
        }
        
        try
        {
            var commandMessage = await _queueService.ReceiveCommandAsync(agentId, tenantId, TimeSpan.FromSeconds(60), ct);
            
            if (commandMessage == null)
            {
                // No messages available
                return Ok(new { command = (object?)null });
            }
            
            // Check dequeue count for poison message handling
            if (commandMessage.DequeueCount > 5)
            {
                _logger.LogWarning("[COMMANDS/RECEIVE] Command {CommandId} exceeded max dequeue count, moving to poison queue", 
                    commandMessage.CommandId);
                
                // Mark as failed in SQL
                var cmd = await _db.AgentCommands.FirstOrDefaultAsync(c => c.Id == commandMessage.CommandId, ct);
                if (cmd != null)
                {
                    cmd.CompletedUtc = DateTime.UtcNow;
                    cmd.Success = false;
                    cmd.Message = $"Exceeded maximum retry count ({commandMessage.DequeueCount})";
                    await _db.SaveChangesAsync(ct);
                    
                    // Move to poison queue
                    await _queueService.MoveToPoisonQueueAsync(agentId, tenantId, cmd, ct);
                }
                
                // Delete from main queue
                await _queueService.DeleteCommandAsync(agentId, tenantId, commandMessage.MessageId, commandMessage.PopReceipt, ct);
                
                // Return no command to agent
                return Ok(new { command = (object?)null });
            }
            
            _logger.LogInformation("[COMMANDS/RECEIVE] Returning command {CommandId} to agent {AgentId}", 
                commandMessage.CommandId, agentId);
            
            return Ok(new 
            { 
                command = new 
                {
                    commandId = commandMessage.CommandId,
                    deviceId = commandMessage.DeviceId,
                    verb = commandMessage.Verb,
                    payload = commandMessage.Payload,
                    messageId = commandMessage.MessageId,
                    popReceipt = commandMessage.PopReceipt
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[COMMANDS/RECEIVE] Error receiving command for agent {AgentId}", agentId);
            return StatusCode(500, new { error = "failed_to_receive_command" });
        }
    }
    
    public sealed class AcknowledgeCommandRequest 
    { 
        public string MessageId { get; set; } = string.Empty;
        public string PopReceipt { get; set; } = string.Empty;
    }
    
    /// <summary>
    /// Acknowledge successful command receipt and delete from queue
    /// </summary>
    [HttpPost("commands/acknowledge")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> AcknowledgeCommand([FromBody] AcknowledgeCommandRequest req, CancellationToken ct)
    {
        var agentIdClaim = GetAgentIdFromClaims();
        var tenantIdClaim = User.Claims.FirstOrDefault(c => c.Type == "tenantId" || c.Type.EndsWith("/tenantId"))?.Value;
        
        if (!Guid.TryParse(agentIdClaim, out var agentId) || !Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            _logger.LogWarning("[COMMANDS/ACK] Invalid token claims: sub={Sub}, tenantId={TenantId}", agentIdClaim, tenantIdClaim);
            return Unauthorized(new { error = "invalid_token_claims" });
        }
        
        try
        {
            await _queueService.DeleteCommandAsync(agentId, tenantId, req.MessageId, req.PopReceipt, ct);
            _logger.LogInformation("[COMMANDS/ACK] Command message {MessageId} acknowledged and deleted for agent {AgentId}", 
                req.MessageId, agentId);
            return Ok(new { ok = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[COMMANDS/ACK] Failed to acknowledge command message {MessageId} for agent {AgentId}", 
                req.MessageId, agentId);
            return StatusCode(500, new { error = "failed_to_acknowledge_command" });
        }
    }
}
