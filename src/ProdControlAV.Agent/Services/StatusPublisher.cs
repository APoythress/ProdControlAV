using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProdControlAV.Core.Models;

namespace ProdControlAV.Agent.Services;

public interface IStatusPublisher
{
    Task PublishAsync(DeviceStatus status, CancellationToken ct);
    Task HeartbeatAsync(DeviceStatus[] snapshot, CancellationToken ct);
}

public sealed record DeviceStatus(string Id, string Name, string Ip, string State, DateTimeOffset ChangedAtUtc, int? PingMs);

public sealed class StatusUploadRequest
{
    public Guid? TenantId { get; set; }
    public List<StatusReading> Readings { get; set; } = new();
}

public sealed class HeartbeatRequest
{
    public string AgentKey { get; set; }
    public string Hostname { get; set; }
    public string? IpAddress { get; set; }
    public string? Version { get; set; }
}

public sealed class StatusPublisher : IStatusPublisher
{
    private readonly HttpClient _http;
    private readonly ILogger<StatusPublisher> _logger;
    private readonly ApiOptions _api;
    private readonly IJwtAuthService _jwtAuth;
    private readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public StatusPublisher(HttpClient http, ILogger<StatusPublisher> logger, IOptions<ApiOptions> api, IJwtAuthService jwtAuth)
    {
        _http = http;
        _logger = logger;
        _api = api.Value;
        _jwtAuth = jwtAuth;
        _http.BaseAddress = new Uri(_api.BaseUrl);
    }

    public async Task PublishAsync(DeviceStatus status, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Publishing status update for device {Id} ({Name} - {Ip}): {State}", status.Id, status.Name, status.Ip, status.State);
            var token = await _jwtAuth.GetValidTokenAsync(ct);
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogWarning("Failed to obtain valid JWT token for status publishing");
                return;
            }
            var dto = new
            {
                TenantId = _api.TenantId,
                DeviceId = status.Id,
                Status = status.State,
                LatencyMs = status.PingMs,
                ObservedAt = DateTimeOffset.UtcNow
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, _api.StatusEndpoint);
            req.Content = JsonContent.Create(dto, options: _json);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var res = await _http.SendAsync(req, ct);
            res.EnsureSuccessStatusCode();
            _logger.LogInformation("State change posted successfully: {Name} {Ip} -> {State}", status.Name, status.Ip, status.State);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish status for device {DeviceId}: {Error}", status.Id, ex.Message);
        }
    }

    public async Task HeartbeatAsync(DeviceStatus[] snapshot, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_api.HeartbeatEndpoint)) return;
        try
        {
            var request = new HeartbeatRequest
            {
                AgentKey = _api.ApiKey ?? "",
                Hostname = Environment.MachineName,
                IpAddress = null,
                Version = "1.0.001"
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, _api.HeartbeatEndpoint);
            req.Content = JsonContent.Create(request, options: _json);
            var res = await _http.SendAsync(req, ct);
            res.EnsureSuccessStatusCode();
            _logger.LogDebug("Heartbeat sent successfully");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send heartbeat: {Error}", ex.Message);
        }
    }
}