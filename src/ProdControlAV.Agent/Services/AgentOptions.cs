namespace ProdControlAV.Agent.Services;

public sealed class AgentOptions
{
    public required int IntervalMs { get; init; }
    public required int Concurrency { get; init; }
    public required int PingTimeoutMs { get; init; }
    public int? TcpFallbackPort { get; init; }
    public required int FailuresToDown { get; init; }
    public required int SuccessesToUp { get; init; }
    public required int HeartbeatSeconds { get; init; }
}

public sealed class ApiOptions
{
    public required string BaseUrl { get; init; }
    public required string DevicesEndpoint { get; init; }
    public required string StatusEndpoint { get; init; }
    public string? HeartbeatEndpoint { get; init; }
    public string? ApiKey { get; init; }
    public int RefreshDevicesSeconds { get; init; } = 30;
}
