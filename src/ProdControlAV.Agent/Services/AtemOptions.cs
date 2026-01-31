namespace ProdControlAV.Agent.Services;

/// <summary>
/// Agent-level ATEM configuration options.
/// Device-specific settings (IP, Port, Name, Transition defaults) are stored in the Devices table per tenant.
/// These settings apply globally to all ATEM connections managed by this agent.
/// </summary>
public class AtemOptions
{
    /// <summary>
    /// Enable automatic reconnection on connection failure.
    /// </summary>
    public bool ReconnectEnabled { get; set; } = true;
    
    /// <summary>
    /// Minimum delay in seconds before attempting reconnect.
    /// </summary>
    public int ReconnectMinDelaySeconds { get; set; } = 2;
    
    /// <summary>
    /// Maximum delay in seconds between reconnect attempts (exponential backoff cap).
    /// </summary>
    public int ReconnectMaxDelaySeconds { get; set; } = 60;
    
    /// <summary>
    /// Minimum interval in milliseconds for publishing state updates (coalescing).
    /// </summary>
    public int StatePublishIntervalMs { get; set; } = 500;
    
    /// <summary>
    /// Only emit state updates when values change (reduces noise).
    /// </summary>
    public bool StateEmitOnChangeOnly { get; set; } = true;
}
