using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProdControlAV.API.Models;
using ProdControlAV.Core.Interfaces;
using ProdControlAV.Core.Models;
using ProdControlAV.Infrastructure.Services;
using AtemInputDto = ProdControlAV.API.Models.AtemInputDto;
using AtemStateDto = ProdControlAV.API.Models.AtemStateDto;

namespace ProdControlAV.API.Controllers;

[ApiController]
[Authorize(Policy = "TenantMember")]
// [Authorize(Policy = "AtemControl")]
[Route("api/[controller]")]
public class AtemController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantProvider _tenant;
    private readonly ILogger<AtemController> _logger;
    private readonly IAgentCommandQueueService _queueService;
    private readonly AzureAtemStateStore _atemStateStore;

    public AtemController(
        AppDbContext db,
        ITenantProvider tenant,
        ILogger<AtemController> logger,
        IAgentCommandQueueService queueService,
        AzureAtemStateStore atemStateStore)
    {
        _db = db;
        _tenant = tenant;
        _logger = logger;
        _queueService = queueService;
        _atemStateStore = atemStateStore;
    }

    /// <summary>
    /// Get ATEM state including available inputs and destinations with current sources
    /// </summary>
    /// <param name="deviceId">Device ID</param>
    /// <param name="ct">Cancellation token</param>
    [HttpGet("{deviceId}/state")]
    public async Task<ActionResult<AtemStateDto>> GetState(Guid deviceId, CancellationToken ct)
    {
        var device = await _db.Devices
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == deviceId, ct);

        if (device == null)
            return NotFound(new { message = "Device not found" });

        if (device.Type != "ATEM" && device.Type != "Switcher")
            return BadRequest(new { message = "Device is not an ATEM switcher" });

        if (device.AtemEnabled != true)
            return BadRequest(new { message = "ATEM control is not enabled for this device" });

        // Get cached ATEM state from Azure Table Storage
        var cachedState = await _atemStateStore.GetStateAsync(_tenant.TenantId, deviceId, ct);

        if (cachedState == null)
        {
            return NotFound(new { message = "ATEM state not available. The agent may not have queried this device yet." });
        }

        // Convert to API DTO format using JSON roundtrip to avoid ambiguous type resolution
        var inputs = JsonSerializer.Deserialize<List<AtemInputDto>>(JsonSerializer.Serialize(cachedState.Inputs))
                     ?? new List<AtemInputDto>();

        var state = new AtemStateDto
        {
            Inputs = inputs,
            Destinations = new List<AtemDestinationDto>
            {
                new() { Id = "Program", Name = "Program", CurrentInputId = cachedState.CurrentSources.GetValueOrDefault("Program") },
                new() { Id = "Aux1", Name = "Aux 1", CurrentInputId = cachedState.CurrentSources.GetValueOrDefault("Aux1") },
                new() { Id = "Aux2", Name = "Aux 2", CurrentInputId = cachedState.CurrentSources.GetValueOrDefault("Aux2") },
                new() { Id = "Aux3", Name = "Aux 3", CurrentInputId = cachedState.CurrentSources.GetValueOrDefault("Aux3") }
            }
        };

        return Ok(state);
    }

    [HttpPost("{deviceId}/state")]
    [AllowAnonymous] // or adjust policy to match how agents authenticate
    public async Task<IActionResult> PostState(Guid deviceId, [FromBody] JsonElement payload, CancellationToken ct)
    {
        var device = await _db.Devices
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == deviceId, ct);

        if (device == null)
            return NotFound(new { message = "Device not found" });

        if (device.Type != "ATEM" && device.Type != "Switcher")
            return BadRequest(new { message = "Device is not an ATEM switcher" });

        if (device.AtemEnabled != true)
            return BadRequest(new { message = "ATEM control is not enabled for this device" });

        // Deserialize incoming JSON into stable storage model
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        AtemStateStorageModel? state;
        try
        {
            state = JsonSerializer.Deserialize<AtemStateStorageModel>(payload.GetRawText(), options);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize ATEM state for device {DeviceId}", deviceId);
            return BadRequest(new { message = "Invalid ATEM state payload" });
        }

        if (state == null)
            return BadRequest(new { message = "Empty ATEM state payload" });

        // Ensure metadata
        state.TimestampUtc = DateTime.UtcNow;

        // Prepare inputs and current sources for storage (JSON-based conversions avoid ambiguous type resolution)
        var infraInputs = JsonSerializer.Deserialize<List<Infrastructure.Services.AtemInputDto>>(JsonSerializer.Serialize(state.Inputs, options))
                          ?? new List<Infrastructure.Services.AtemInputDto>();

        var infraCurrent = state.CurrentSources?.ToDictionary(
            kvp => kvp.Key,
            kvp => long.TryParse(kvp.Value, out var result) ? result : (long?)null)
            ?? new Dictionary<string, long?>();

        // Persist to your state store
        try
        {
            await _atemStateStore.UpsertStateAsync(
                _tenant.TenantId,
                deviceId,
                infraInputs,
                infraCurrent,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save ATEM state for device {DeviceId}", deviceId);
            return StatusCode(500, new { message = "Failed to persist ATEM state" });
        }

        return Ok(new { message = "State persisted" });
    }


    /// <summary>
    /// Execute a CUT transition to switch input on destination
    /// </summary>
    /// <param name="deviceId">Device ID</param>
    /// <param name="request">Control request with destination and input ID</param>
    /// <param name="ct">Cancellation token</param>
    [HttpPost("{deviceId}/cut")]
    public async Task<ActionResult<AtemControlResponse>> Cut(
        Guid deviceId,
        [FromBody] AtemControlRequest request,
        CancellationToken ct)
    {
        var device = await _db.Devices
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == deviceId, ct);

        if (device == null)
            return NotFound(new { message = "Device not found" });

        if (device.Type != "ATEM" && device.Type != "Switcher")
            return BadRequest(new { message = "Device is not an ATEM switcher" });

        if (device.AtemEnabled != true)
            return BadRequest(new { message = "ATEM control is not enabled for this device" });

        // Get the agent ID for this device
        var agent = await _db.Agents
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.TenantId == _tenant.TenantId, ct);

        if (agent == null)
            return BadRequest(new { message = "No agent found for this tenant" });

        _logger.LogInformation(
            "ATEM CUT command - Device: {DeviceId}, Destination: {Destination}, Input: {InputId}",
            deviceId, request.Destination, request.InputId);

        // Queue command for agent to execute
        var payload = new
        {
            commandType = "ATEM",
            atemCommand = GetAtemCommandForDestination(request.Destination, "CUT"),
            deviceId = deviceId.ToString(),
            deviceIp = device.Ip,
            devicePort = device.Port,
            inputId = request.InputId,
            destination = request.Destination
        };

        var command = new AgentCommand
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            AgentId = agent.Id,
            DeviceId = deviceId,
            Verb = "ATEM_CUT",
            Payload = JsonSerializer.Serialize(payload),
            CreatedUtc = DateTime.UtcNow,
            DueUtc = DateTime.UtcNow
        };

        await _queueService.EnqueueCommandAsync(command, ct);

        _logger.LogInformation("Queued ATEM CUT command {CommandId} for agent {AgentId}", command.Id, agent.Id);

        return Ok(new AtemControlResponse
        {
            Success = true,
            Message = $"CUT command queued for input {request.InputId} on {request.Destination}"
        });
    }

    /// <summary>
    /// Execute an AUTO transition (with fade) to switch input on destination
    /// </summary>
    /// <param name="deviceId">Device ID</param>
    /// <param name="request">Control request with destination and input ID</param>
    /// <param name="ct">Cancellation token</param>
    [HttpPost("{deviceId}/auto")]
    public async Task<ActionResult<AtemControlResponse>> Auto(
        Guid deviceId,
        [FromBody] AtemControlRequest request,
        CancellationToken ct)
    {
        var device = await _db.Devices
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == deviceId, ct);

        if (device == null)
            return NotFound(new { message = "Device not found" });

        if (device.Type != "ATEM" && device.Type != "Switcher")
            return BadRequest(new { message = "Device is not an ATEM switcher" });

        if (device.AtemEnabled != true)
            return BadRequest(new { message = "ATEM control is not enabled for this device" });

        // Get the agent ID for this device
        var agent = await _db.Agents
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.TenantId == _tenant.TenantId, ct);

        if (agent == null)
            return BadRequest(new { message = "No agent found for this tenant" });

        _logger.LogInformation(
            "ATEM AUTO command - Device: {DeviceId}, Destination: {Destination}, Input: {InputId}",
            deviceId, request.Destination, request.InputId);

        var transitionRate = device.AtemTransitionDefaultRate ?? 30;

        // Queue command for agent to execute
        var payload = new
        {
            commandType = "ATEM",
            atemCommand = GetAtemCommandForDestination(request.Destination, "AUTO"),
            deviceId = deviceId.ToString(),
            deviceIp = device.Ip,
            devicePort = device.Port,
            inputId = request.InputId,
            destination = request.Destination,
            transitionRate = transitionRate
        };

        var command = new AgentCommand
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            AgentId = agent.Id,
            DeviceId = deviceId,
            Verb = "ATEM_AUTO",
            Payload = JsonSerializer.Serialize(payload),
            CreatedUtc = DateTime.UtcNow,
            DueUtc = DateTime.UtcNow
        };

        await _queueService.EnqueueCommandAsync(command, ct);

        _logger.LogInformation("Queued ATEM AUTO command {CommandId} for agent {AgentId}", command.Id, agent.Id);

        return Ok(new AtemControlResponse
        {
            Success = true,
            Message = $"AUTO transition command queued for input {request.InputId} on {request.Destination} (rate: {transitionRate} frames)"
        });
    }

    /// <summary>
    /// Map destination to ATEM command name
    /// </summary>
    private string GetAtemCommandForDestination(string destination, string transition)
    {
        // For Program, use the main commands
        if (destination.Equals("Program", StringComparison.OrdinalIgnoreCase))
        {
            return transition == "CUT" ? "CUT_TO_PROGRAM" : "FADE_TO_PROGRAM";
        }

        // For Aux outputs, use set_aux commands
        return transition == "CUT" ? $"SET_AUX_{destination.ToUpperInvariant()}" : $"FADE_AUX_{destination.ToUpperInvariant()}";
    }
}
