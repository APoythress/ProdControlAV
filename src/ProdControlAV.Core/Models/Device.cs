using System;

namespace ProdControlAV.Core.Models;

public class Device
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Model { get; set; }
    public string Brand { get; set; }
    public string Type { get; set; }
    public bool AllowTelNet { get; set; }
    public string Ip { get; set; }
    public int Port { get; set; }
    public string? Location { get; set; }
    public Guid TenantId { get; set; }
    public bool Status { get; set; }
    public DateTimeOffset? LastChecked { get; set; }
    public string? LastResponse { get; set; }
    public int PingFrequencySeconds { get; set; } = 300; // Default to 300 seconds (5 minutes)
    public bool? RecordingStatus { get; set; } // For Video devices - true when recording, false when idle
}