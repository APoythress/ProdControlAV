using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ProdControlAV.Agent.Services;

public interface IStatusPublisher
{
    Task PublishAsync(DeviceStatus status, CancellationToken ct);
    Task HeartbeatAsync(IEnumerable<DeviceStatus> snapshot, CancellationToken ct);
}

public sealed record DeviceStatus(string Id, string Name, string Ip, string State, DateTimeOffset ChangedAtUtc);

public sealed class StatusUploadRequest
{
    public string AgentKey { get; set; } = string.Empty;
    public Guid? TenantId { get; set; }
    public List<StatusReading> Readings { get; set; } = new();
}

public sealed class StatusReading
{
    public string DeviceId { get; set; } = default!;
    public bool IsOnline { get; set; }
    public int? LatencyMs { get; set; }
    public string? Message { get; set; }
}

public sealed class HeartbeatRequest
{
    public string AgentKey { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? Version { get; set; }
}

public sealed class StatusPublisher : IStatusPublisher
{
    private readonly HttpClient _http;
    private readonly ILogger<StatusPublisher> _logger;
    private readonly ApiOptions _api;
    private readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public StatusPublisher(HttpClient http, ILogger<StatusPublisher> logger, Microsoft.Extensions.Options.IOptions<ApiOptions> api)
    {
        _http = http;
        _logger = logger;
        _api = api.Value;
        _http.BaseAddress = new Uri(_api.BaseUrl);
    }

    public async Task PublishAsync(DeviceStatus status, CancellationToken ct)
    {
        var request = new StatusUploadRequest
        {
            AgentKey = _api.ApiKey ?? "",
            TenantId = null, // Let the API determine the tenant from the agent key
            Readings = new List<StatusReading>
            {
                new StatusReading
                {
                    DeviceId = status.Id,
                    IsOnline = status.State == "ONLINE",
                    LatencyMs = null,
                    Message = $"{status.Name} ({status.Ip}) is {status.State}"
                }
            }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, _api.StatusEndpoint)
        {
            Content = JsonContent.Create(request, options: _json)
        };

        var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        _logger.LogInformation("State change posted: {Name} {Ip} -> {State}", status.Name, status.Ip, status.State);
    }

    public async Task HeartbeatAsync(IEnumerable<DeviceStatus> snapshot, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_api.HeartbeatEndpoint)) return;

        var request = new HeartbeatRequest
        {
            AgentKey = _api.ApiKey ?? "",
            Hostname = Environment.MachineName,
            IpAddress = null, // Could be determined dynamically if needed
            Version = "1.0.0"
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, _api.HeartbeatEndpoint)
        {
            Content = JsonContent.Create(request, options: _json)
        };

        var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        _logger.LogDebug("Heartbeat sent successfully");
    }
}