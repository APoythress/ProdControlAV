using System;

namespace ProdControlAV.Core.Models;

public sealed class DeviceTargetDto
{
    public Guid Id { get; set; }
    public string IpAddress { get; set; } = default!;
    public string? Type { get; set; }
    public int? TcpPort { get; set; }
    public int PingFrequencySeconds { get; set; } = 300; // Default to 300 seconds (5 minutes)
}

public sealed class StatusReading
{
    public string DeviceId { get; set; } = default!;
    public bool IsOnline { get; set; }
    public int? LatencyMs { get; set; }
    public string? Message { get; set; }
}

public sealed class CommandEnvelope
{
    public Guid CommandId { get; set; } = default!;
    public Guid DeviceId { get; set; } = default!;
    public string Verb { get; set; } = default!;
    public string? Payload { get; set; }
}