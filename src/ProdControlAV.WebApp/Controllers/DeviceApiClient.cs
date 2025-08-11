using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using ProdControlAV.Core.Models;
public class DeviceApiClient
{
    private readonly HttpClient _http;

    public DeviceApiClient(HttpClient http)
    {
        _http = http;
    }
    public Task<List<DeviceStatus>?> GetDevicesAsync()
        => _http.GetFromJsonAsync<List<DeviceStatus>>("api/devices");

    public Task<List<DeviceAction>?> GetActionsAsync()
        => _http.GetFromJsonAsync<List<DeviceAction>>("api/actions");
    
    public async Task<string> AddNewDeviceAsync(DeviceModel device)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "api/devices")
        {
            Content = JsonContent.Create(device)
        };
        Console.WriteLine(device);
        Console.WriteLine(request);
        var response = await _http.SendAsync(request);
        return response.IsSuccessStatusCode ? "Success" : await response.Content.ReadAsStringAsync();
    }

    public async Task<string> SendCommandAsync(Guid deviceId, string command)
    {
        var response = await _http.PostAsJsonAsync("api/commands", new { deviceId, command });
        return response.IsSuccessStatusCode ? "Success" : await response.Content.ReadAsStringAsync();
    }

    public async Task<string> PingDeviceAsync(Guid deviceId)
    {
        var response = await _http.PostAsJsonAsync("api/devices", new { deviceId });
        return response.IsSuccessStatusCode ? "Success" : await response.Content.ReadAsStringAsync();
    }
}