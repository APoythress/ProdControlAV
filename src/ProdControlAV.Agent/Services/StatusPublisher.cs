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
        using var req = new HttpRequestMessage(HttpMethod.Post, _api.StatusEndpoint)
        {
            Content = JsonContent.Create(status, options: _json)
        };
        if (!string.IsNullOrWhiteSpace(_api.ApiKey))
            req.Headers.Add("X-Api-Key", _api.ApiKey);

        var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        _logger.LogInformation("State change posted: {Name} {Ip} -> {State}", status.Name, status.Ip, status.State);
    }

    public async Task HeartbeatAsync(IEnumerable<DeviceStatus> snapshot, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_api.HeartbeatEndpoint)) return;

        using var req = new HttpRequestMessage(HttpMethod.Post, _api.HeartbeatEndpoint)
        {
            Content = JsonContent.Create(snapshot, options: _json)
        };
        if (!string.IsNullOrWhiteSpace(_api.ApiKey))
            req.Headers.Add("X-Api-Key", _api.ApiKey);

        var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
    }
}
