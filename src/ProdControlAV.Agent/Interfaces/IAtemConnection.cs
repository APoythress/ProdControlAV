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
public interface IAtemConnection { }
