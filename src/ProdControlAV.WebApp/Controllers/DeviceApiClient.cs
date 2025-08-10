using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using ProdControlAV.Core.Models; // use your actual namespace for DeviceStatus / DeviceAction
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
}