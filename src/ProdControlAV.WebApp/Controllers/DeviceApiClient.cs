using System;
using System.Collections.Generic;
using System.Linq;
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
        await _http.GetFromJsonAsync<IEnumerable<Device>>("api/devices", ct) ?? Enumerable.Empty<Device>();
    
    public async Task<List<Device>> GetDevices(CancellationToken ct = default)
    {
        var devices = await _http.GetFromJsonAsync<IEnumerable<Device>>("api/devices/devices", ct);
        return devices?.ToList() ?? new List<Device>();
    }
    
    public async Task<IEnumerable<DeviceAction>> GetDeviceActionsAsync(CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<IEnumerable<DeviceAction>>("api/devices/actions", ct) ?? Enumerable.Empty<DeviceAction>();

    public async Task AddNewDeviceAsync(Device device, CancellationToken ct = default) =>
        await _http.PostAsync("api/devices", JsonContent.Create(device), ct);
    
    public async Task UpdateDevice(Device device, CancellationToken ct = default)
    {
        var dto = new 
        {
            Id = device.Id,
            Name = device.Name,
            Model = device.Model,
            Brand = device.Brand,
            Type = device.Type,
            AllowTelNet = device.AllowTelNet,
            Ip = device.Ip,
            Port = device.Port,
            Location = device.Location,
            PingFrequencySeconds = device.PingFrequencySeconds
        };
        var response = await _http.PutAsJsonAsync($"api/devices/{device.Id}", dto, ct);
        response.EnsureSuccessStatusCode();
    }
}