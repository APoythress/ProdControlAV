using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProdControlAV.Core.Models;
using AgentDevice = ProdControlAV.Agent.Models.Device;

namespace ProdControlAV.Agent.Services;

public interface IDeviceSource
{
    IReadOnlyCollection<AgentDevice> Current { get; }
    Task RefreshAsync(CancellationToken ct);
}

public sealed class DeviceSource : BackgroundService, IDeviceSource
{
    private readonly HttpClient _http;
    private readonly ILogger<DeviceSource> _logger;
    private readonly ApiOptions _api;
    private readonly IJwtAuthService _jwtAuth;
    private readonly List<AgentDevice> _devices = new();
    private readonly object _gate = new();
    private readonly PeriodicTimer _timer;

    public IReadOnlyCollection<AgentDevice> Current
    {
        get { lock(_gate) return _devices.ToArray(); }
    }

    public DeviceSource(HttpClient http, ILogger<DeviceSource> logger, IOptions<ApiOptions> api, IJwtAuthService jwtAuth)
    {
        _http = http;
        _logger = logger;
        _api = api.Value;
        _jwtAuth = jwtAuth;
        _http.BaseAddress = new Uri(_api.BaseUrl);
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, _api.RefreshDevicesSeconds)));
    }

    public async Task RefreshAsync(CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Refreshing device list from API at {Endpoint}", _api.DevicesEndpoint);
            
            // Get valid JWT token
            var token = await _jwtAuth.GetValidTokenAsync(ct);
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogWarning("Failed to obtain valid JWT token for device refresh");
                return;
            }

            _logger.LogDebug("Sending GET request to {BaseUrl}{Endpoint} with JWT token", _api.BaseUrl, _api.DevicesEndpoint);

            using var req = new HttpRequestMessage(HttpMethod.Get, _api.DevicesEndpoint);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            using var res = await _http.SendAsync(req, ct);
            
            if (!res.IsSuccessStatusCode)
            {
                var errorBody = await res.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Failed to fetch devices from API: {StatusCode} - {ReasonPhrase}. Response: {ErrorBody}", 
                    res.StatusCode, res.ReasonPhrase, errorBody);
                return;
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var deviceTargets = await res.Content.ReadFromJsonAsync<List<DeviceTargetDto>>(options, ct) ?? new List<DeviceTargetDto>();

            // Convert DeviceTargetDto to AgentDevice
            var devices = deviceTargets.Select(dt => new AgentDevice
            {
                Id = dt.Id.ToString(),
                Name = dt.IpAddress, // Use IP as name for now since Name is not in DTO
                Ip = dt.IpAddress,
                PreferTcp = dt.TcpPort.HasValue,
                PingFrequencySeconds = dt.PingFrequencySeconds,
                Type = dt.Type
            }).ToList();

            lock (_gate)
            {
                _devices.Clear();
                _devices.AddRange(devices);
            }
            
            _logger.LogInformation("Device list refreshed successfully: {Count} devices loaded", devices.Count);
            if (devices.Count > 0)
            {
                _logger.LogDebug("Devices: {Devices}", string.Join(", ", devices.Select(d => $"{d.Name} ({d.Ip})")));
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Device refresh cancelled");
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error while refreshing device list from API: {Error}", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while refreshing device list from API: {Error}", ex.Message);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DeviceSource background service starting - will refresh every {RefreshSeconds} seconds", _api.RefreshDevicesSeconds);
        
        // initial fetch
        await RefreshAsync(stoppingToken);

        while (await _timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshAsync(stoppingToken);
        }
    }
}
