using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using ProdControlAV.Core.Models;

namespace ProdControlAV.Server.Controllers.Device_Management;

public class DeviceManagerController
{
    private readonly HttpClient _http;

    public DeviceManagerController(HttpClient http)
    {
        _http = http;
    }

    public async Task<string> AddNewDeviceAsync(DeviceModel device, string csrfToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "api/devices")
        {
            Content = JsonContent.Create(device)
        };
        request.Headers.Add("X-CSRF-TOKEN", csrfToken);

        var response = await _http.SendAsync(request);
        if (response.IsSuccessStatusCode)
            return "Success";

        return await response.Content.ReadAsStringAsync();
    }
}