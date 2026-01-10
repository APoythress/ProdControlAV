namespace ProdControlAV.Agent.Services;

/// <summary>
/// Configuration options for ATEM device connection and operation.
/// </summary>
public class AtemOptions
{
    /// <summary>
    /// IPv4 address or resolvable hostname of the ATEM device.
    /// </summary>
    public string Ip { get; set; } = string.Empty;
    
    /// <summary>
    /// Port number for ATEM connection (default: 9910 per ATEM protocol).
    /// </summary>
    public int Port { get; set; } = 9910;
    
    /// <summary>
    /// Friendly name for the ATEM device (used in logs and telemetry).
    /// </summary>
    public string? Name { get; set; }
    
    /// <summary>
    /// Enable automatic connection on service startup.
    /// </summary>
    public bool ConnectAuto { get; set; } = true;
    
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
    /// Connection timeout in seconds.
    /// </summary>
    public int ConnectTimeoutSeconds { get; set; } = 10;
    
    /// <summary>
    /// Minimum interval in milliseconds for publishing state updates (coalescing).
    /// </summary>
    public int StatePublishIntervalMs { get; set; } = 500;
    
    /// <summary>
    /// Only emit state updates when values change (reduces noise).
    /// </summary>
    public bool StateEmitOnChangeOnly { get; set; } = true;
    
    /// <summary>
    /// Default transition type: "mix" (fade) or "cut".
    /// </summary>
    public string TransitionDefaultType { get; set; } = "mix";
    
    /// <summary>
    /// Default transition rate in frames (ATEM uses 1-250, where 1 = fastest).
    /// Typical values: 30 frames @ 30fps = 1 second transition.
    /// </summary>
    public int TransitionDefaultRate { get; set; } = 30;
}
