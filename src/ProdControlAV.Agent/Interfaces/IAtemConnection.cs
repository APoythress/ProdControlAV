using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ProdControlAV.Agent.Interfaces;

/// <summary>
/// Connection state for ATEM device.
/// </summary>
public enum AtemConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Degraded // Optional: repeated timeouts but still receiving partial state
}

/// <summary>
/// Represents the current state of an ATEM switcher.
/// </summary>
public class AtemState
{
    public int ProgramInputId { get; set; }
    public int PreviewInputId { get; set; }
    public string? LastTransitionType { get; set; }
    public int? LastTransitionRate { get; set; }
    public bool InTransition { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Represents an ATEM macro.
/// </summary>
public class AtemMacro
{
    public int MacroId { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Interface for ATEM connection management.
/// </summary>
public interface IAtemConnection
{
    /// <summary>
    /// Current connection state.
    /// </summary>
    AtemConnectionState ConnectionState { get; }
    
    /// <summary>
    /// Current known state of the ATEM switcher.
    /// </summary>
    AtemState? CurrentState { get; }
    
    /// <summary>
    /// Event raised when connection state changes.
    /// </summary>
    event EventHandler<AtemConnectionState>? ConnectionStateChanged;
    
    /// <summary>
    /// Event raised when ATEM state changes.
    /// </summary>
    event EventHandler<AtemState>? StateChanged;
    
    /// <summary>
    /// Connect to the ATEM device.
    /// </summary>
    Task ConnectAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Disconnect from the ATEM device.
    /// </summary>
    Task DisconnectAsync();
    
    /// <summary>
    /// Perform an immediate cut transition to the specified program input.
    /// </summary>
    /// <param name="programInputId">Input ID to switch to</param>
    /// <param name="ct">Cancellation token</param>
    Task CutToProgramAsync(int programInputId, CancellationToken ct = default);
    
    /// <summary>
    /// Perform a mix/fade transition to the specified program input.
    /// </summary>
    /// <param name="programInputId">Input ID to transition to</param>
    /// <param name="transitionRate">Transition rate in frames (optional, uses default if not specified)</param>
    /// <param name="ct">Cancellation token</param>
    Task FadeToProgramAsync(int programInputId, int? transitionRate = null, CancellationToken ct = default);
    
    /// <summary>
    /// Set the preview input to the specified source.
    /// </summary>
    /// <param name="previewInputId">Input ID to set as preview</param>
    /// <param name="ct">Cancellation token</param>
    Task SetPreviewAsync(int previewInputId, CancellationToken ct = default);
    
    /// <summary>
    /// List available macros.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of available macros</returns>
    Task<List<AtemMacro>> ListMacrosAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Run a macro by ID.
    /// </summary>
    /// <param name="macroId">Macro ID to execute</param>
    /// <param name="ct">Cancellation token</param>
    Task RunMacroAsync(int macroId, CancellationToken ct = default);
}
