using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using ProdControlAV.Core.Models;
using ProdControlAV.Infrastructure.Services;
using ProdControlAV.WebApp.Models;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;

namespace ProdControlAV.WebApp.Controllers
{
    public class DevicesController : ControllerBase
    {
        private HttpClient _httpClient = new();

        public async Task<string> GetDeviceNameAsync(Guid deviceId)
        {
            var device = await _httpClient.GetFromJsonAsync<Device>("api/devices/devices");
            return device?.Name ?? "Unknown Device";
        }
    }
}