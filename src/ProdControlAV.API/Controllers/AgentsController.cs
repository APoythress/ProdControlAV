using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProdControlAV.API.Models;
using ProdControlAV.API.Services;
using ProdControlAV.Core.Models;
using ProdControlAV.Core.Interfaces;
using ProdControlAV.Infrastructure.Services;
using static Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults;

namespace ProdControlAV.API.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/agents")]
public sealed class AgentsController : ControllerBase
{
    // Default ping frequency when not available from Table Storage (will be added to schema in future)
    private const int DefaultPingFrequencySeconds = 300;
    
    private readonly AppDbContext _db;
    private readonly IAgentAuth _auth;
    private readonly IJwtService _jwtService;
    private readonly ILogger<AgentsController> _logger;
    private readonly IAgentCommandQueueService _queueService;
    private readonly IDeviceStatusStore _statusStore;
    private readonly IDeviceStore _deviceStore;
    private readonly IActivityMonitor _activityMonitor;
    private readonly IAgentAuthStore _agentAuthStore;
    
    public AgentsController(
        AppDbContext db, 
        IAgentAuth auth, 
        IJwtService jwtService, 
        ILogger<AgentsController> logger,
        IAgentCommandQueueService queueService,
        IDeviceStatusStore statusStore,
        IDeviceStore deviceStore,
        IActivityMonitor activityMonitor,
        IAgentAuthStore agentAuthStore) 
    { 
        _db = db; 
        _auth = auth;
        _jwtService = jwtService;
        _logger = logger;
        _queueService = queueService;
        _statusStore = statusStore;
        _deviceStore = deviceStore;
        _activityMonitor = activityMonitor;
        _agentAuthStore = agentAuthStore;
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
        
        // Record agent activity
        await _activityMonitor.RecordAgentActivityAsync(agent.Id.ToString(), agent.TenantId.ToString(), ct);
        
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

    /// <summary>
    /// Agent heartbeat endpoint - uses JWT authentication to avoid repeated agent key validation.
    /// Agents should authenticate once per 8 hours to get a JWT token, then use that token for all subsequent API calls.
    /// </summary>
    [HttpPost("heartbeat")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Heartbeat([FromBody] HeartbeatRequest req, CancellationToken ct)
    {
        // Extract agent ID and tenant ID from JWT claims (already authenticated)
        var agentIdClaim = GetAgentIdFromClaims();
        var tenantIdClaim = User.Claims.FirstOrDefault(c => c.Type == "tenantId" || c.Type.EndsWith("/tenantId"))?.Value;
        
        if (!Guid.TryParse(agentIdClaim, out var agentId) || !Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            _logger.LogWarning("[HEARTBEAT] Invalid token claims: sub={Sub}, tenantId={TenantId}", agentIdClaim, tenantIdClaim);
            return Unauthorized(new { error = "invalid_token_claims" });
        }
        
        _logger.LogInformation("[HEARTBEAT] Received heartbeat from authenticated agent: AgentId={AgentId}, Hostname={Hostname}, IpAddress={IpAddress}, Version={Version}", 
            agentId, req.Hostname, req.IpAddress, req.Version);
        
        // Record agent activity in Table Storage (for idle monitoring)
        await _activityMonitor.RecordAgentActivityAsync(agentId.ToString(), tenantId.ToString(), ct);
        
        // Update agent metadata in Table Storage (no SQL hit needed - we trust the JWT)
        // This keeps the Table Store metadata fresh for auth lookups
        await _agentAuthStore.UpdateAgentMetadataAsync(agentId, tenantId, req.Hostname, req.IpAddress, req.Version, ct);
        _logger.LogDebug("[HEARTBEAT] Agent metadata updated in Table Storage: AgentId={AgentId}", agentId);
        
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
        
        // Use Table Storage instead of SQL for device list
        var devices = new List<DeviceTargetDto>();
        await foreach (var device in _deviceStore.GetAllForTenantAsync(tenantId, ct))
        {
            devices.Add(new DeviceTargetDto
            {
                Id = device.Id,
                IpAddress = device.IpAddress,
                Type = device.Type,
                TcpPort = null,
                PingFrequencySeconds = DefaultPingFrequencySeconds // PingFrequencySeconds not yet in Table Storage
            });
        }
        
        _logger.LogInformation("[DEVICES] Returning {Count} devices from Table Storage for TenantId={TenantId}", devices.Count, tenantId);
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

        if (req is null)
        {
            // Model binding failed; log raw body for debugging and return 400 so callers can correct payload
            try
            {
                Request.EnableBuffering();
                Request.Body.Position = 0;
                using var sr = new System.IO.StreamReader(Request.Body, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                var raw = await sr.ReadToEndAsync(ct);
                Request.Body.Position = 0;
                _logger.LogWarning("[STATUS] Model binding returned null. Raw body: {RawBody}", raw);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[STATUS] Failed to read raw request body when model binding returned null");
            }

            return BadRequest(new { error = "invalid_or_missing_body" });
        }

        _logger.LogInformation("[STATUS] Body: {Body}", req);

        try
        {
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

            if (req.Readings == null || req.Readings.Count == 0)
            {
                _logger.LogWarning("[STATUS] Empty readings in status upload");
                return BadRequest(new { error = "empty_readings" });
            }

            var now = DateTime.UtcNow;
            var saved = 0;

            // Save status to Azure Table Storage for device status and update the Devices table projection
            foreach (var r in req.Readings)
            {
                if (Guid.TryParse(r.DeviceId, out var deviceGuid))
                {
                    var statusString = r.IsOnline ? "ONLINE" : "OFFLINE";
                    try
                    {
                        await _statusStore.UpsertAsync(tenantId, deviceGuid, statusString, r.LatencyMs, DateTimeOffset.UtcNow, ct);
                        await _deviceStore.UpsertStatusAsync(tenantId, deviceGuid, statusString, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, ct);
                        saved++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[STATUS] Failed projecting status to table storage for TenantId={TenantId} DeviceId={DeviceId}", tenantId, deviceGuid);
                    }
                }
            }
            
            _logger.LogInformation("[STATUS] Saved {Count} status readings for TenantId={TenantId}", saved, tenantId);
            return Ok(new { ok = true, saved });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[STATUS] Failed saving status readings: {Error}", ex.Message);
            return StatusCode(500, new { error = "failed_to_save_status" });
        }
    }

    public sealed class CommandPullRequest { public int Max { get; set; } = 10; }
    public sealed class CommandPullResponse { public List<CommandEnvelope> Commands { get; set; } = new(); }

    /// <summary>
    /// Legacy SQL-based command polling endpoint. DEPRECATED: Use /commands/receive instead for queue-based polling.
    /// This endpoint will be removed in a future version. It causes SQL load and should not be used in production.
    /// </summary>
    [HttpPost("commands/next")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [Obsolete("Use /api/agents/commands/receive instead. This endpoint polls SQL directly and should not be used in production.")]
    public async Task<ActionResult<CommandPullResponse>> Next([FromBody] CommandPullRequest req, CancellationToken ct)
    {
        _logger.LogWarning("[COMMANDS/NEXT] DEPRECATED endpoint called. This endpoint polls SQL directly and causes unnecessary load. Migrate to /api/agents/commands/receive for queue-based polling.");
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
                    cmd.Message = "Moved to poison queue after exceeding dequeue count";
                    await _db.SaveChangesAsync(ct);
                }

                // Move message to poison queue storage if available
                try { if (cmd != null) await _queueService.MoveToPoisonQueueAsync(agentId, tenantId, cmd, ct); } catch { /* ignore */ }

                return Ok(new { command = (object?)null });
            }

            // Normal delivery path: translate queue message to CommandEnvelope and return
            var dbCmd = await _db.AgentCommands.FirstOrDefaultAsync(c => c.Id == commandMessage.CommandId, ct);
            if (dbCmd == null)
            {
                // No corresponding DB record; just acknowledge and return null
                _logger.LogWarning("[COMMANDS/RECEIVE] No DB record for command {CommandId}", commandMessage.CommandId);
                return Ok(new { command = (object?)null });
            }

            _logger.LogInformation("[COMMANDS/RECEIVE] Returning command {CommandId} to agent {AgentId}", dbCmd.Id, agentId);

            var response = new CommandPullResponse
            {
                Commands = new List<CommandEnvelope>
                {
                    new CommandEnvelope
                    {
                        CommandId = dbCmd.Id,
                        DeviceId = dbCmd.DeviceId,
                        Verb = dbCmd.Verb,
                        Payload = dbCmd.Payload
                    }
                }
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[COMMANDS/RECEIVE] Error receiving command for agent {AgentId}", agentId);
            return StatusCode(500, new { error = "failed_to_receive_command" });
        }
    }

    /// <summary>
    /// Poll CommandQueue table for pending commands (Table Storage-based)
    /// </summary>
    [HttpPost("commands/poll")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Policy = "JwtAgent")]
    public async Task<IActionResult> PollCommandQueue(CancellationToken ct)
    {
        var tenantIdClaim = User.FindFirst("tenantId")?.Value;

        if (!Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            _logger.LogWarning("[COMMANDS/POLL] Invalid token claims: tenantId={TenantId}", tenantIdClaim);
            return Unauthorized(new { error = "invalid_token_claims" });
        }

        try
        {
            var queueStore = HttpContext.RequestServices.GetRequiredService<ICommandQueueStore>();
            
            // First, check for stuck processing commands (stuck for more than 5 minutes) and reset them
            var stuckCommands = new List<CommandQueueDto>();
            await foreach (var cmd in queueStore.GetStuckProcessingCommandsAsync(tenantId, TimeSpan.FromMinutes(5), ct))
            {
                stuckCommands.Add(cmd);
            }
            
            foreach (var stuckCmd in stuckCommands)
            {
                _logger.LogWarning("[COMMANDS/POLL] Found stuck command {CommandId} (attempt {AttemptCount}), resetting to Pending", 
                    stuckCmd.CommandId, stuckCmd.AttemptCount);
                await queueStore.ResetToPendingAsync(tenantId, stuckCmd.CommandId, ct);
            }
            
            // Get all pending commands for this tenant from Table Storage
            var pendingCommands = new List<CommandQueueDto>();
            await foreach (var cmd in queueStore.GetPendingForTenantAsync(tenantId, ct))
            {
                pendingCommands.Add(cmd);
            }

            // Return first command if available
            if (pendingCommands.Any())
            {
                var firstCmd = pendingCommands.First();
                
                // Check if command has exceeded max retry attempts (3 attempts)
                // AttemptCount logic:
                //   - AttemptCount=0: First poll, will become attempt 1
                //   - AttemptCount=1: Second poll, will become attempt 2
                //   - AttemptCount=2: Third poll, will become attempt 3
                //   - AttemptCount=3: Already attempted 3 times, mark as failed
                if (firstCmd.AttemptCount >= 3)
                {
                    _logger.LogWarning("[COMMANDS/POLL] Command {CommandId} exceeded max retry attempts ({AttemptCount}), marking as failed", 
                        firstCmd.CommandId, firstCmd.AttemptCount);
                    
                    // Mark as failed in CommandQueue
                    await queueStore.MarkAsFailedAsync(tenantId, firstCmd.CommandId, ct);
                    
                    // Record failure in CommandHistory
                    var historyStore = HttpContext.RequestServices.GetRequiredService<ICommandHistoryStore>();
                    var failureHistory = new CommandHistoryDto(
                        ExecutionId: Guid.NewGuid(),
                        CommandId: firstCmd.CommandId,
                        TenantId: tenantId,
                        DeviceId: firstCmd.DeviceId,
                        CommandName: firstCmd.CommandName,
                        ExecutedUtc: DateTimeOffset.UtcNow,
                        Success: false,
                        ErrorMessage: $"Command failed after {firstCmd.AttemptCount} attempts",
                        Response: null,
                        HttpStatusCode: null,
                        ExecutionTimeMs: null
                    );
                    await historyStore.RecordExecutionAsync(failureHistory, ct);
                    
                    // Return null to indicate no command available (agent will continue polling)
                    return Ok(new { command = (CommandEnvelope?)null });
                }
                
                // Capture current attempt count before incrementing
                var currentAttempt = firstCmd.AttemptCount + 1; // Will be attempt 1, 2, or 3
                
                // Mark as processing and increment attempt count
                await queueStore.MarkAsProcessingAsync(tenantId, firstCmd.CommandId, ct);
                
                _logger.LogInformation("[COMMANDS/POLL] Returning command {CommandId} for execution (attempt {AttemptCount} of 3)", 
                    firstCmd.CommandId, currentAttempt);
                
                var envelope = new CommandEnvelope
                {
                    CommandId = firstCmd.CommandId,
                    DeviceId = firstCmd.DeviceId,
                    Verb = firstCmd.CommandType, // REST or Telnet
                    Payload = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        commandName = firstCmd.CommandName,
                        commandType = firstCmd.CommandType,
                        commandData = firstCmd.CommandData,
                        httpMethod = firstCmd.HttpMethod,
                        requestBody = firstCmd.RequestBody,
                        requestHeaders = firstCmd.RequestHeaders,
                        deviceIp = firstCmd.DeviceIp,
                        devicePort = firstCmd.DevicePort,
                        deviceType = firstCmd.DeviceType,
                        monitorRecordingStatus = firstCmd.MonitorRecordingStatus,
                        statusEndpoint = firstCmd.StatusEndpoint,
                        statusPollingIntervalSeconds = firstCmd.StatusPollingIntervalSeconds
                    })
                };

                return Ok(new { command = envelope });
            }

            return Ok(new { command = (CommandEnvelope?)null });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[COMMANDS/POLL] Error polling command queue for tenant {TenantId}", tenantId);
            return StatusCode(500, new { error = "failed_to_poll_queue" });
        }
    }

    /// <summary>
    /// Record command execution result in CommandHistory table (Table Storage)
    /// </summary>
    [HttpPost("commands/history")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Policy = "JwtAgent")]
    public async Task<IActionResult> RecordCommandHistory([FromBody] CommandHistoryRequest req, CancellationToken ct)
    {
        var agentIdClaim = User.FindFirst("sub")?.Value ?? User.FindFirst("agentId")?.Value;
        var tenantIdClaim = User.FindFirst("tenantId")?.Value;

        if (!Guid.TryParse(agentIdClaim, out var agentId) || !Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            _logger.LogWarning("[COMMANDS/HISTORY] Invalid token claims: sub={Sub}, tenantId={TenantId}", agentIdClaim, tenantIdClaim);
            return Unauthorized(new { error = "invalid_token_claims" });
        }

        try
        {
            var historyStore = HttpContext.RequestServices.GetRequiredService<ICommandHistoryStore>();
            var queueStore = HttpContext.RequestServices.GetRequiredService<ICommandQueueStore>();
            
            var history = new CommandHistoryDto(
                ExecutionId: Guid.NewGuid(),
                CommandId: req.CommandId,
                TenantId: tenantId,
                DeviceId: req.DeviceId,
                CommandName: req.CommandName ?? "Unknown",
                ExecutedUtc: DateTimeOffset.UtcNow,
                Success: req.Success,
                ErrorMessage: req.ErrorMessage,
                Response: req.Response,
                HttpStatusCode: req.HttpStatusCode,
                ExecutionTimeMs: req.ExecutionTimeMs
            );

            // Record execution history
            Exception? historyException = null;
            try
            {
                await historyStore.RecordExecutionAsync(history, ct);
                _logger.LogInformation("[COMMANDS/HISTORY] Recorded execution for command {CommandId}, Success={Success}", 
                    req.CommandId.ToString("D"), req.Success);
            }
            catch (Exception ex)
            {
                historyException = ex;
                _logger.LogError(ex, "[COMMANDS/HISTORY] Error recording command history for command {CommandId}", 
                    req.CommandId.ToString("D"));
            }
            
            // Mark command status as Succeeded or Failed BEFORE dequeuing
            // This ensures proper status tracking in the queue
            Exception? statusException = null;
            try
            {
                if (req.Success)
                {
                    await queueStore.MarkAsSucceededAsync(tenantId, req.CommandId, ct);
                    _logger.LogInformation("[COMMANDS/HISTORY] Marked command {CommandId} as Succeeded", req.CommandId.ToString("D"));
                }
                else
                {
                    await queueStore.MarkAsFailedAsync(tenantId, req.CommandId, ct);
                    _logger.LogInformation("[COMMANDS/HISTORY] Marked command {CommandId} as Failed", req.CommandId.ToString("D"));
                }
            }
            catch (Exception ex)
            {
                statusException = ex;
                _logger.LogError(ex, "[COMMANDS/HISTORY] Error marking command {CommandId} status (Success={Success})", 
                    req.CommandId.ToString("D"), req.Success);
            }
            
            // Always attempt to remove command from queue after marking status
            // This prevents commands from getting stuck in the queue
            try
            {
                await queueStore.DequeueAsync(tenantId, req.CommandId, ct);
                _logger.LogInformation("[COMMANDS/HISTORY] Dequeued command {CommandId}", req.CommandId.ToString("D"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[COMMANDS/HISTORY] Error dequeuing command {CommandId}. Command may remain in queue.", 
                    req.CommandId.ToString("D"));
            }
            
            // If history recording or status marking failed, return appropriate error
            if (historyException != null || statusException != null)
            {
                var errors = new List<string>();
                if (historyException != null) errors.Add("history recording failed");
                if (statusException != null) errors.Add("status update failed");
                
                return StatusCode(500, new { 
                    error = "partial_failure", 
                    message = $"Command was dequeued but {string.Join(" and ", errors)}"
                });
            }
            
            return Ok(new { success = true, executionId = history.ExecutionId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[COMMANDS/HISTORY] Unexpected error in RecordCommandHistory for command {CommandId}", 
                req.CommandId.ToString("D"));
            return StatusCode(500, new { error = "unexpected_error" });
        }
    }

    public sealed class CommandHistoryRequest
    {
        public Guid CommandId { get; set; }
        public Guid DeviceId { get; set; }
        public string? CommandName { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Response { get; set; }
        public int? HttpStatusCode { get; set; }
        public double? ExecutionTimeMs { get; set; }
    }
    
    /// <summary>
    /// Request for updating recording status of a Video device
    /// </summary>
    public sealed class UpdateRecordingStatusRequest
    {
        public Guid DeviceId { get; set; }
        public bool RecordingStatus { get; set; }
    }
    
    /// <summary>
    /// Updates the recording status for a Video device in Table Storage
    /// POST /api/agents/recording-status
    /// </summary>
    [HttpPost("recording-status")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateRecordingStatus([FromBody] UpdateRecordingStatusRequest request, CancellationToken ct)
    {
        try
        {
            // Extract tenant ID from JWT claims
            var tenantIdClaim = User.Claims.FirstOrDefault(c => c.Type == "tenantId" || c.Type.EndsWith("/tenantId"))?.Value;
            if (!Guid.TryParse(tenantIdClaim, out var tenantId))
            {
                _logger.LogWarning("[RECORDING-STATUS] Invalid tenant claim: {TenantIdClaim}", tenantIdClaim);
                return Unauthorized(new { error = "invalid_token_claims" });
            }
            
            // Validate request
            if (request.DeviceId == Guid.Empty)
            {
                return BadRequest(new { error = "DeviceId is required" });
            }
            
            // Update recording status in Table Storage
            await _deviceStore.UpsertRecordingStatusAsync(tenantId, request.DeviceId, request.RecordingStatus, ct);
            
            _logger.LogInformation("[RECORDING-STATUS] Updated recording status for device {DeviceId} to {RecordingStatus}", 
                request.DeviceId, request.RecordingStatus);
            
            return Ok(new { success = true, deviceId = request.DeviceId, recordingStatus = request.RecordingStatus });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RECORDING-STATUS] Error updating recording status for device {DeviceId}", request.DeviceId);
            return StatusCode(500, new { error = "Failed to update recording status", details = ex.Message });
        }
    }
}
