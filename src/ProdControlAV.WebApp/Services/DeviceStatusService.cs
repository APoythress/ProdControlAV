using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using ProdControlAV.WebApp.Models;

namespace ProdControlAV.WebApp.Services;

public class DeviceStatusService
{
    private readonly HttpClient _http;

    public DeviceStatusService(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<DeviceStatusDto>> GetStatusesAsync(Guid tenantId)
    {
        var url = $"https://your-api-host.com/api/status?tenantId={tenantId}";
        var result = await _http.GetFromJsonAsync<StatusListDto>(url);
        return result != null && result.Items != null ? new List<DeviceStatusDto>(result.Items) : new List<DeviceStatusDto>();
    }
}
