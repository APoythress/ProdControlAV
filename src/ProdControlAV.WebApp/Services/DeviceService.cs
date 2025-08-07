using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using ProdControlAV.WebApp.Models;

namespace ProdControlAV.WebApp.Services
{
    public class DeviceService
    {
        private readonly HttpClient _http;

        public DeviceService(HttpClient http)
        {
            _http = http;
        }

        public async Task<List<DeviceStatusDto>> GetAllDevicesAsync()
        {
            return await _http.GetFromJsonAsync<List<DeviceStatusDto>>("/api/devices")
                   ?? new List<DeviceStatusDto>();
        }

        public async Task<long> PingDeviceAsync(string ip)
        {
            var result = await _http.GetAsync($"/api/devices/ping?ip={ip}");
            return result.IsSuccessStatusCode
                ? long.Parse(await result.Content.ReadAsStringAsync())
                : -1;
        }

        public async Task SendCommandAsync(string ip, string command)
        {
            await _http.PostAsJsonAsync("/api/devices/command", new { ip, command });
        }
    }
}