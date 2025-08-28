using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using ProdControlAV.API.Controllers;
using ProdControlAV.Core.Models;
using Device = ProdControlAV.Agent.Models.Device;

namespace ProdControlAV.Agent.Services;

public class CommandService
{
    private readonly HttpClient _http;
    private readonly ILogger<DeviceSource> _logger;
    private readonly ApiOptions _api;
    private readonly Device _device;
    private readonly object _gate = new();
    private readonly AppDbContext _db;

    public CommandService(HttpClient http, ILogger<DeviceSource> logger, IOptions<ApiOptions> api)
    {
        _http = http;
        _logger = logger;
        _api = api.Value;
        _http.BaseAddress = new Uri(_api.BaseUrl);
    }

    public async Task RunAsync(DeviceAction action)
    {
        
    }
}