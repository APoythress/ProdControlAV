using System.Net.Http.Json;

namespace ProdControlAV.Agent.Services;

public sealed class AtemStatePublisher
{
    private readonly HttpClient _http;
    private readonly ILogger<AtemStatePublisher> _logger;
    private readonly Guid _deviceId;
    private const string EndpointTemplate = "https://prodcontrol.app/api/atem/{0}/state"; // HACK - setting explicit endpoint for now

    public AtemStatePublisher(HttpClient httpClient, ILogger<AtemStatePublisher> logger, Guid deviceId)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _deviceId = deviceId;
    }
    
    public async Task PublishAsync(object state, CancellationToken ct = default)
    {
        if (state is null) return;

        var relativeUrl = string.Format(EndpointTemplate, _deviceId);

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, relativeUrl)
            {
                Content = JsonContent.Create(state)
            };

            var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
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
            _logger.LogDebug("Publish canceled for {DeviceId}", _deviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while publishing ATEM state for {DeviceId}", _deviceId);
        }
    }

}