using System;

namespace ProdControlAV.Core.Models;

/// <summary>
/// Configuration options for the activity monitor system
/// </summary>
public class ActivityMonitorOptions
{
    /// <summary>
    /// How long to wait (in minutes) with no activity before considering the system idle.
    /// Default: 10 minutes
    /// </summary>
    public int IdleTimeoutMinutes { get; set; } = 10;

    /// <summary>
    /// How often (in seconds) background services should check idle status.
    /// Default: 30 seconds
    /// </summary>
    public int CheckIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Whether idle suspension is enabled. Set to false to disable the feature.
    /// Default: true
    /// </summary>
    public bool EnableIdleSuspension { get; set; } = true;

    /// <summary>
    /// Azure Table Storage connection string for distributed activity tracking.
    /// If not provided, activity monitor will use in-memory tracking (single instance only).
    /// </summary>
    public string? TableStorageConnectionString { get; set; }

    /// <summary>
    /// Azure Table Storage endpoint URI for distributed activity tracking.
    /// Alternative to connection string when using managed identity.
    /// </summary>
    public string? TableStorageEndpoint { get; set; }

    /// <summary>
    /// List of operation types that should bypass idle suspension (e.g., "Alarms", "Alerts").
    /// These operations will run even when the system is idle.
    /// </summary>
    public string[] CriticalOperations { get; set; } = Array.Empty<string>();
}
