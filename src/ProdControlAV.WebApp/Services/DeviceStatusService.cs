using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using ProdControlAV.Core.Models;

namespace ProdControlAV.WebApp.Services;

public class DeviceStatusService
{
    private readonly HttpClient _http;

    public DeviceStatusService(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<DeviceStatus>> GetStatusesAsync()
    {
        return await _http.GetFromJsonAsync<List<DeviceStatus>>("https://your-api-host.com/api/status")
               ?? new List<DeviceStatus>();
    }
}
