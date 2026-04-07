using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ProdControlAV.Agent.Services;

public sealed class AtemStatePublisher
{
    private readonly HttpClient _http;
    private readonly ILogger<AtemStatePublisher> _logger;
    private readonly Guid _deviceId;
    private const string EndpointTemplate = "api/atem/{0}/state";
    private readonly JwtAuthService _jwtAuth; 

    public AtemStatePublisher(HttpClient httpClient, ILogger<AtemStatePublisher> logger, Guid deviceId, JwtAuthService jwtAuth)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _deviceId = deviceId;
        _jwtAuth = jwtAuth;
    }

    public async Task PublishAsync(object state, CancellationToken ct = default)
    {
        if (state is null)
            return;

        var url = string.Format(EndpointTemplate, _deviceId);
        string? token = null;

        if (_jwtAuth != null)
        {
            try
            {
                token = await _jwtAuth.GetValidTokenAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get valid JWT token for publishing ATEM state");
            }
        }
        
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(state)
            };

            if (!string.IsNullOrEmpty(token))
            {
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }

            var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Failed to publish ATEM state for {DeviceId}. Status: {StatusCode}, Body: {Body}",
                    _deviceId, resp.StatusCode, body);
            }
            else
            {
                _logger.LogDebug("Published ATEM state for {DeviceId}", _deviceId);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // normal cancellation on shutdown
            _logger.LogDebug("Publish canceled for {DeviceId}", _deviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while publishing ATEM state for {DeviceId}", _deviceId);
            throw;
        }
    }
}