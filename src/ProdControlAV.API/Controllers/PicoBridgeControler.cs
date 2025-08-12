// Controllers/PicoBridgeController.cs
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/pico")]
public class PicoBridgeController : ControllerBase
{
    private readonly HttpClient _http;
    private readonly string _picoBase;

    public PicoBridgeController(IHttpClientFactory factory, IConfiguration cfg)
    {
        _http = factory.CreateClient();
        _picoBase = cfg["Pico:BaseUrl"] ?? "http://192.168.1.50/";
    }

    [HttpGet("devices")]
    public async Task<IActionResult> GetDevices()
    {
        var resp = await _http.GetAsync($"{_picoBase}api/devices");
        var body = await resp.Content.ReadAsStringAsync();
        return Content(body, "application/json");
    }

    [HttpPost("devices")]
    public async Task<IActionResult> Create([FromBody] object payload)
    {
        var resp = await _http.PostAsJsonAsync($"{_picoBase}api/devices", payload);
        var body = await resp.Content.ReadAsStringAsync();
        return StatusCode((int)resp.StatusCode, body);
    }

    [HttpPut("devices/{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] object payload)
    {
        var resp = await _http.PutAsJsonAsync($"{_picoBase}api/devices/{id}", payload);
        var body = await resp.Content.ReadAsStringAsync();
        return StatusCode((int)resp.StatusCode, body);
    }

    [HttpDelete("devices/{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var resp = await _http.DeleteAsync($"{_picoBase}api/devices/{id}");
        var body = await resp.Content.ReadAsStringAsync();
        return StatusCode((int)resp.StatusCode, body);
    }
}