using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProdControlAV.API.Auth;
using ProdControlAV.API.Models;
using ProdControlAV.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProdControlAV.API.Controllers;

/// <summary>
/// Controller for ATEM switcher control operations
/// </summary>
[ApiController]
[Authorize(Policy = "TenantMember")]
[Authorize(Policy = "AtemControl")]
[Route("api/[controller]")]
public class AtemController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantProvider _tenant;
    private readonly ILogger<AtemController> _logger;

    public AtemController(AppDbContext db, ITenantProvider tenant, ILogger<AtemController> logger)
    {
        _db = db;
        _tenant = tenant;
        _logger = logger;
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
            .FirstOrDefaultAsync(d => d.Id == deviceId && d.TenantId == _tenant.TenantId, ct);

        if (device == null)
            return NotFound(new { message = "Device not found" });

        if (device.Type != "ATEM" && device.Type != "Switcher")
            return BadRequest(new { message = "Device is not an ATEM switcher" });

        if (device.AtemEnabled != true)
            return BadRequest(new { message = "ATEM control is not enabled for this device" });

        // TODO: In a real implementation, this would query the ATEM via Agent API
        // For now, return mock data based on common ATEM configurations
        var state = new AtemStateDto
        {
            Inputs = GetMockInputs(),
            Destinations = GetMockDestinations()
        };

        return Ok(state);
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
            .FirstOrDefaultAsync(d => d.Id == deviceId && d.TenantId == _tenant.TenantId, ct);

        if (device == null)
            return NotFound(new { message = "Device not found" });

        if (device.Type != "ATEM" && device.Type != "Switcher")
            return BadRequest(new { message = "Device is not an ATEM switcher" });

        if (device.AtemEnabled != true)
            return BadRequest(new { message = "ATEM control is not enabled for this device" });

        _logger.LogInformation(
            "ATEM CUT command - Device: {DeviceId}, Destination: {Destination}, Input: {InputId}",
            deviceId, request.Destination, request.InputId);

        // TODO: In a real implementation, this would send command to ATEM via Agent API
        // For now, return success
        return Ok(new AtemControlResponse
        {
            Success = true,
            Message = $"CUT to input {request.InputId} on {request.Destination}"
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
            .FirstOrDefaultAsync(d => d.Id == deviceId && d.TenantId == _tenant.TenantId, ct);

        if (device == null)
            return NotFound(new { message = "Device not found" });

        if (device.Type != "ATEM" && device.Type != "Switcher")
            return BadRequest(new { message = "Device is not an ATEM switcher" });

        if (device.AtemEnabled != true)
            return BadRequest(new { message = "ATEM control is not enabled for this device" });

        _logger.LogInformation(
            "ATEM AUTO command - Device: {DeviceId}, Destination: {Destination}, Input: {InputId}",
            deviceId, request.Destination, request.InputId);

        // TODO: In a real implementation, this would send command to ATEM via Agent API
        // For now, return success with transition rate from device settings
        var transitionRate = device.AtemTransitionDefaultRate ?? 30;
        
        return Ok(new AtemControlResponse
        {
            Success = true,
            Message = $"AUTO transition to input {request.InputId} on {request.Destination} (rate: {transitionRate} frames)"
        });
    }

    /// <summary>
    /// Mock input data for ATEM switcher
    /// In production, this would come from the actual ATEM device
    /// </summary>
    private List<AtemInputDto> GetMockInputs()
    {
        return new List<AtemInputDto>
        {
            new() { InputId = 1, Name = "Camera 1", Type = "SDI" },
            new() { InputId = 2, Name = "Camera 2", Type = "SDI" },
            new() { InputId = 3, Name = "Camera 3", Type = "SDI" },
            new() { InputId = 4, Name = "Camera 4", Type = "SDI" },
            new() { InputId = 5, Name = "HDMI 1", Type = "HDMI" },
            new() { InputId = 6, Name = "HDMI 2", Type = "HDMI" },
            new() { InputId = 7, Name = "Graphics", Type = "Internal" },
            new() { InputId = 8, Name = "Media Player", Type = "Internal" }
        };
    }

    /// <summary>
    /// Mock destination data for ATEM switcher
    /// In production, this would come from the actual ATEM device
    /// </summary>
    private List<AtemDestinationDto> GetMockDestinations()
    {
        return new List<AtemDestinationDto>
        {
            new() { Id = "Program", Name = "Program", CurrentInputId = 1 },
            new() { Id = "Aux1", Name = "Aux 1", CurrentInputId = 2 },
            new() { Id = "Aux2", Name = "Aux 2", CurrentInputId = 3 },
            new() { Id = "Aux3", Name = "Aux 3", CurrentInputId = 4 }
        };
    }
}
