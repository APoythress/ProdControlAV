using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ProdControlAV.Agent.Models;

namespace ProdControlAV.Agent.Services;

public sealed class DeviceTargetDto
{
    public Guid Id { get; set; }
    public string IpAddress { get; set; } = default!;
    public string? Type { get; set; }
    public int? TcpPort { get; set; }
}

public interface IDeviceSource
{
    IReadOnlyCollection<Device> Current { get; }
    Task RefreshAsync(CancellationToken ct);
}

public sealed class DeviceSource : BackgroundService, IDeviceSource
{
    private readonly HttpClient _http;
    private readonly ILogger<DeviceSource> _logger;
    private readonly ApiOptions _api;
    private readonly List<Device> _devices = new();
    private readonly object _gate = new();
    private readonly PeriodicTimer _timer;

    public IReadOnlyCollection<Device> Current
    {
        get { lock(_gate) return _devices.ToArray(); }
    }

    public DeviceSource(HttpClient http, ILogger<DeviceSource> logger, IOptions<ApiOptions> api)
    {
        _http = http;
        _logger = logger;
        _api = api.Value;
        _http.BaseAddress = new Uri(_api.BaseUrl);
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, _api.RefreshDevicesSeconds)));
    }

    public async Task RefreshAsync(CancellationToken ct)
    {
        try
        {
            var url = $"{_api.DevicesEndpoint}?agentKey={Uri.EscapeDataString(_api.ApiKey ?? "")}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);

            using var res = await _http.SendAsync(req, ct);
            res.EnsureSuccessStatusCode();

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var deviceTargets = await res.Content.ReadFromJsonAsync<List<DeviceTargetDto>>(options, ct) ?? new List<DeviceTargetDto>();

            // Convert DeviceTargetDto to Device
            var devices = deviceTargets.Select(dt => new Device
            {
                Id = dt.Id.ToString(),
                Name = dt.IpAddress, // Use IP as name for now since Name is not in DTO
                Ip = dt.IpAddress,
                PreferTcp = dt.TcpPort.HasValue
            }).ToList();

            lock (_gate)
            {
                _devices.Clear();
                _devices.AddRange(devices);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh device list from API");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // initial fetch
        await RefreshAsync(stoppingToken);

        while (await _timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshAsync(stoppingToken);
        }
    }
}
