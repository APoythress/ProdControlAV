using System;

namespace ProdControlAV.Core.Models;

/// <summary>
/// Represents a pre-defined command template that users can select to add to their devices.
/// This is a static/read-only table populated with common HyperDeck REST API commands.
/// </summary>
public class CommandTemplate
{
    /// <summary>
    /// Unique identifier for the command template
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Category of the command (e.g., "Transport Control", "Recording", "Configuration")
    /// </summary>
    public string Category { get; set; } = string.Empty;
    
    /// <summary>
    /// User-friendly name of the command
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Detailed description of what the command does
    /// </summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// HTTP method (GET, POST, PUT, DELETE)
    /// </summary>
    public string HttpMethod { get; set; } = "GET";
    
    /// <summary>
    /// The endpoint path or command string (e.g., "/transport/play", "/transport/stop")
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;
    
    /// <summary>
    /// Optional payload/body for POST/PUT requests (JSON format)
    /// </summary>
    public string? Payload { get; set; }
    
    /// <summary>
    /// Device type this template applies to (e.g., "HyperDeck", "ATEM", etc.)
    /// </summary>
    public string DeviceType { get; set; } = "HyperDeck";
    
    /// <summary>
    /// Order for display in UI
    /// </summary>
    public int DisplayOrder { get; set; }
    
    /// <summary>
    /// Whether this template is currently active/visible
    /// </summary>
    public bool IsActive { get; set; } = true;

    // ── ATEM-specific template fields ──────────────────────────────────────────

    /// <summary>
    /// For ATEM templates: the ATEM function to execute
    /// (e.g., "CutToProgram", "FadeToProgram", "SetPreview", "SetAux", "RunMacro",
    /// "GetProgramInput", "GetPreviewInput", "GetAuxSource", "ListMacros")
    /// </summary>
    public string? AtemFunction { get; set; }

    /// <summary>
    /// For ATEM templates: default input ID (used by CutToProgram, FadeToProgram, SetPreview, SetAux)
    /// </summary>
    public int? AtemInputId { get; set; }

    /// <summary>
    /// For ATEM templates: default transition rate in frames (used by FadeToProgram; 0 means use device default)
    /// </summary>
    public int? AtemTransitionRate { get; set; }

    /// <summary>
    /// For ATEM templates: default macro ID (used by RunMacro)
    /// </summary>
    public int? AtemMacroId { get; set; }

    /// <summary>
    /// For ATEM templates: auxiliary output channel index, 0-based (used by SetAux and GetAuxSource)
    /// </summary>
    public int? AtemChannel { get; set; }
}
