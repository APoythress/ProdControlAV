using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ProdControlAV.Agent.Models;

namespace ProdControlAV.Agent.Services;

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
            using var req = new HttpRequestMessage(HttpMethod.Get, _api.DevicesEndpoint);
            if (!string.IsNullOrWhiteSpace(_api.ApiKey))
                req.Headers.Add("X-Api-Key", _api.ApiKey);

            using var res = await _http.SendAsync(req, ct);
            res.EnsureSuccessStatusCode();

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var list = await res.Content.ReadFromJsonAsync<List<Device>>(options, ct) ?? new List<Device>();

            lock (_gate)
            {
                _devices.Clear();
                _devices.AddRange(list);
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
