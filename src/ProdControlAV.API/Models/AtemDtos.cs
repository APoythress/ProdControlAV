using System;
using System.Collections.Generic;

namespace ProdControlAV.API.Models;

/// <summary>
/// DTO for ATEM input source information
/// </summary>
public class AtemInputDto
{
    public long InputId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // e.g., "Camera", "HDMI", "SDI"
}

/// <summary>
/// DTO for ATEM destination information
/// </summary>
public class AtemDestinationDto
{
    public string Id { get; set; } = string.Empty; // e.g., "Program", "Aux1", "Aux2", "Aux3"
    public string Name { get; set; } = string.Empty;
    public long? CurrentInputId { get; set; }
}

/// <summary>
/// DTO for ATEM state information
/// </summary>
public class AtemStateDto
{
    public List<AtemInputDto> Inputs { get; set; } = new();
    public List<AtemDestinationDto> Destinations { get; set; } = new();
}

/// <summary>
/// DTO for ATEM control requests
/// </summary>
public class AtemControlRequest
{
    public string Destination { get; set; } = string.Empty; // "Program", "Aux1", "Aux2", "Aux3"
    public long InputId { get; set; }
}

/// <summary>
/// DTO for ATEM control response
/// </summary>
public class AtemControlResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// DTO for ATEM snapshot response
/// </summary>
public class AtemSnapshotResponseDto
{
    public Guid DeviceId { get; set; }
    public string AtemResponseString { get; set; }
}
