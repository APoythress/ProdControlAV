using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using ProdControlAV.Core.Models;

public class DeviceApiClient
{
    private readonly HttpClient _http;
    public DeviceApiClient(HttpClient http) => _http = http;

    public async Task<IEnumerable<Device>> GetDevicesAsync(CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<IEnumerable<Device>>("api/devices", ct) ?? [];
    
    public async Task<IEnumerable<DeviceAction>> GetDeviceActionsAsync(CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<IEnumerable<DeviceAction>>("api/devices/actions", ct) ?? [];

    public async Task AddNewDeviceAsync(Device device, CancellationToken ct = default) =>
        await _http.PostAsync("app/devices", JsonContent.Create(device), ct);
}