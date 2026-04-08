using Microsoft.AspNetCore.Authorization;
using ProdControlAV.Core.Models;

namespace ProdControlAV.API.Controllers.Device_Management;

[Authorize(Policy = "Admin")]
public class DeviceManagerController
{
    private readonly HttpClient _http;

    public DeviceManagerController(HttpClient http)
    {
        _http = http;
    }

    public async Task<string> AddNewDeviceAsync(Device device, string csrfToken)
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