namespace ProdControlAV.Agent.Services;

public sealed class AgentOptions
{
    public required int IntervalMs { get; init; } = 10000;
    public required int Concurrency { get; init; }
    public required int PingTimeoutMs { get; init; } = 1000;
    public int? TcpFallbackPort { get; init; }
    public required int FailuresToDown { get; init; }
    public required int SuccessesToUp { get; init; }
    public required int HeartbeatSeconds { get; init; } = 30;
}

public sealed class ApiOptions
{
    public string BaseUrl { get; set; } = string.Empty; // Changed to set for runtime configuration
    public required string DevicesEndpoint { get; init; }
    public required string StatusEndpoint { get; init; }
    public string? HeartbeatEndpoint { get; init; }
    public string? CommandsEndpoint { get; init; }
    public string? CommandCompleteEndpoint { get; init; }
    public string? ApiKey { get; set; } // Changed to set for runtime configuration
    public int RefreshDevicesSeconds { get; init; } = 30;
    public int CommandPollIntervalSeconds { get; init; } = 10;
}
