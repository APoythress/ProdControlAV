using System;
using System.Collections.Generic;

namespace ProdControlAV.API.Models;

/// <summary>
/// Response model for agent health dashboard endpoint
/// </summary>
public sealed class AgentHealthDashboardResponse
{
    /// <summary>
    /// List of agents with their health and status data
    /// </summary>
    public List<AgentHealthInfo> Agents { get; set; } = new();
}

/// <summary>
/// Health and status information for a single agent
/// </summary>
public sealed class AgentHealthInfo
{
    /// <summary>
    /// Unique identifier for the agent
    /// </summary>
    public string AgentId { get; set; } = string.Empty;
    
    /// <summary>
    /// Agent name or hostname
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Status of the agent: "online" if last heartbeat &lt; threshold, else "offline"
    /// </summary>
    public string Status { get; set; } = "offline";
    
    /// <summary>
    /// Timestamp of last heartbeat or activity (UTC)
    /// </summary>
    public DateTime? LastSeenUtc { get; set; }
    
    /// <summary>
    /// Currently running agent version
    /// </summary>
    public string? Version { get; set; }
    
    /// <summary>
    /// Whether the agent is running the latest available version
    /// </summary>
    public bool IsUpToDate { get; set; }
    
    /// <summary>
    /// Latest version available (if update exists)
    /// </summary>
    public string? VersionAvailable { get; set; }
    
    /// <summary>
    /// Number of commands pending execution for this agent
    /// </summary>
    public int CommandsPending { get; set; }
    
    /// <summary>
    /// Number of commands successfully executed in the last 48 hours
    /// </summary>
    public int CommandsPolledSuccessful { get; set; }
    
    /// <summary>
    /// Number of commands unsuccessfully executed in the last 48 hours
    /// </summary>
    public int CommandsPolledUnsuccessful { get; set; }
    
    /// <summary>
    /// Most recent errors (last 48 hours, max 5)
    /// </summary>
    public List<AgentErrorInfo> RecentErrors { get; set; } = new();
}

/// <summary>
/// Error information for agent health dashboard
/// </summary>
public sealed class AgentErrorInfo
{
    /// <summary>
    /// Timestamp when the error occurred (UTC)
    /// </summary>
    public DateTime Timestamp { get; set; }
    
    /// <summary>
    /// Error message
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
