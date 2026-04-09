using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
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
    public string? Hostname { get; set; }
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
    private readonly string _agentVersion;

    public StatusPublisher(HttpClient http, ILogger<StatusPublisher> logger, IOptions<ApiOptions> api, IJwtAuthService jwtAuth)
    {
        _http = http;
        _logger = logger;
        _api = api.Value;
        _jwtAuth = jwtAuth;
        _http.BaseAddress = new Uri(_api.BaseUrl);
        
        // Get the actual agent version from assembly
        var assembly = Assembly.GetExecutingAssembly();
        _agentVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion 
            ?? assembly.GetName().Version?.ToString() 
            ?? "0.0.0";
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
                // Use agent-style bulk upload format expected by API: { tenantId: guid, readings: [ { deviceId, isOnline, latencyMs, message } ] }
                TenantId = _api.TenantId,
                Readings = new[]
                {
                    new {
                        DeviceId = status.Id,
                        IsOnline = string.Equals(status.State, "ONLINE", StringComparison.OrdinalIgnoreCase),
                        LatencyMs = status.PingMs,
                        Message = (string?)null
                    }
                }
             };

            // Log the serialized payload for debugging when API returns 400
            string payloadJson = JsonSerializer.Serialize(dto, _json);
            _logger.LogDebug("Status publish payload: {PayloadJson}", payloadJson);

            using var req = new HttpRequestMessage(HttpMethod.Post, _api.StatusEndpoint);
            req.Content = JsonContent.Create(dto, options: _json);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var res = await _http.SendAsync(req, ct);

            if (!res.IsSuccessStatusCode)
            {
                var errorBody = await res.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Status publish failed: Code={StatusCode} Body={Body}", res.StatusCode, errorBody);
            }

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
            // Get JWT token for authentication (reuses existing token if still valid)
            var token = await _jwtAuth.GetValidTokenAsync(ct);
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogWarning("Failed to obtain valid JWT token for heartbeat");
                return;
            }
            
            var request = new HeartbeatRequest
            {
                Hostname = Environment.MachineName,
                IpAddress = null,
                Version = _agentVersion
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, _api.HeartbeatEndpoint);
            req.Content = JsonContent.Create(request, options: _json);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var res = await _http.SendAsync(req, ct);
            res.EnsureSuccessStatusCode();
            _logger.LogDebug("Heartbeat sent successfully with JWT authentication");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send heartbeat: {Error}", ex.Message);
        }

        // Also send the status snapshot to the agents status endpoint so the API always receives fresh device statuses
        if (string.IsNullOrWhiteSpace(_api.StatusEndpoint)) return;
        try
        {
            var token = await _jwtAuth.GetValidTokenAsync(ct);
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogWarning("Failed to obtain valid JWT token for status snapshot publishing");
                return;
            }

            var readings = snapshot.Select(s => new {
                DeviceId = s.Id,
                IsOnline = string.Equals(s.State, "ONLINE", StringComparison.OrdinalIgnoreCase),
                LatencyMs = s.PingMs,
                Message = (string?)null
            }).ToArray();

            var payload = new {
                TenantId = _api.TenantId,
                Readings = readings
            };

            string payloadJson = JsonSerializer.Serialize(payload, _json);
            _logger.LogDebug("Status snapshot payload: {PayloadJson}", payloadJson);

            using var req2 = new HttpRequestMessage(HttpMethod.Post, _api.StatusEndpoint);
            req2.Content = JsonContent.Create(payload, options: _json);
            req2.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var res2 = await _http.SendAsync(req2, ct);
            if (!res2.IsSuccessStatusCode)
            {
                var errorBody = await res2.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Status snapshot publish failed: Code={StatusCode} Body={Body}", res2.StatusCode, errorBody);
            }
            else
            {
                _logger.LogDebug("Status snapshot published successfully");
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish status snapshot: {Error}", ex.Message);
        }
    }
}