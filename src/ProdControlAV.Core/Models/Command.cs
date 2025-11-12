using System;
using System.ComponentModel.DataAnnotations;

namespace ProdControlAV.Core.Models;

/// <summary>
/// Represents a command definition stored in SQL DB.
/// Command execution and history are stored in Table Storage.
/// </summary>
public class Command
{
    [Key]
    public Guid CommandId { get; set; }
    
    public Guid TenantId { get; set; }
    
    public Guid DeviceId { get; set; }
    
    [Required]
    [MaxLength(200)]
    public string CommandName { get; set; } = default!;
    
    [MaxLength(1000)]
    public string? Description { get; set; }
    
    /// <summary>
    /// Command type: "REST" for HTTP API calls, "Telnet" for telnet commands
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string CommandType { get; set; } = "REST";
    
    /// <summary>
    /// For REST commands: the API endpoint path (e.g., "/api/power/on")
    /// For Telnet commands: the command string to send
    /// </summary>
    [MaxLength(2000)]
    public string? CommandData { get; set; }
    
    /// <summary>
    /// HTTP method for REST commands (GET, POST, PUT, DELETE, PATCH)
    /// </summary>
    [MaxLength(10)]
    public string? HttpMethod { get; set; }
    
    /// <summary>
    /// Optional request body for REST commands (JSON format)
    /// </summary>
    public string? RequestBody { get; set; }
    
    /// <summary>
    /// Optional headers for REST commands (JSON format: {"key":"value"})
    /// </summary>
    public string? RequestHeaders { get; set; }
    
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    
    public DateTimeOffset? UpdatedUtc { get; set; }
    
    /// <summary>
    /// If true, command can only be executed when device is online
    /// </summary>
    public bool RequireDeviceOnline { get; set; } = true;
}
