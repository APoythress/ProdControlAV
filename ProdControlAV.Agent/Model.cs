public sealed class AgentHeartbeatRequest
{
    public string AgentKey { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string Version { get; set; } = "1.0.0";
}

public sealed class DeviceTarget
{
    public Guid Id { get; set; } = default!;
    public string IpAddress { get; set; } = default!;
    public string? Type { get; set; }
    public int? TcpPort { get; set; }
}

public sealed class StatusReading
{
    public Guid DeviceId { get; set; } = default!;
    public bool IsOnline { get; set; }
    public int? LatencyMs { get; set; }
    public string? Message { get; set; }
}

public sealed class StatusUploadRequest
{
    public string AgentKey { get; set; } = string.Empty;
    public Guid? TenantId { get; set; }
    public List<StatusReading> Readings { get; set; } = new();
}

public sealed class CommandEnvelope
{
    public string CommandId { get; set; } = default!;
    public string DeviceId { get; set; } = default!;
    public string Verb { get; set; } = default!;
    public string? Payload { get; set; }
}

public sealed class CommandPullRequest
{
    public string AgentKey { get; set; } = string.Empty;
    public int Max { get; set; } = 10;
}

public sealed class CommandPullResponse
{
    public List<CommandEnvelope> Commands { get; set; } = new();
}

public sealed class CommandCompleteRequest
{
    public string AgentKey { get; set; } = string.Empty;
    public string CommandId { get; set; } = default!;
    public bool Success { get; set; }
    public string? Message { get; set; }
    public int? DurationMs { get; set; }
}