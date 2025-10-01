using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ProdControlAV.Agent.Services;

/// <summary>
/// Request model for agent JWT authentication
/// </summary>
public sealed class AgentAuthRequest
{
    public string AgentKey { get; set; } = string.Empty;
    public Guid? TenantId { get; set; } = Guid.Empty;
}

/// <summary>
/// Response model for JWT authentication
/// </summary>
public sealed class AgentAuthResponse
{
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    [JsonPropertyName("expiresAt")]
    public DateTime ExpiresAt { get; set; }

    [JsonPropertyName("tokenType")]
    public string TokenType { get; set; } = "Bearer";
}

/// <summary>
/// Service for managing JWT authentication tokens for the agent
/// </summary>
public interface IJwtAuthService
{
    /// <summary>
    /// Get a valid JWT token, refreshing if necessary
    /// </summary>
    Task<string?> GetValidTokenAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Force refresh of the current token
    /// </summary>
    Task<bool> RefreshTokenAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Check if the current token is still valid
    /// </summary>
    bool IsTokenValid { get; }
}

/// <summary>
/// JWT authentication service for the agent
/// </summary>
public sealed class JwtAuthService : IJwtAuthService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<JwtAuthService> _logger;
    private readonly ApiOptions _api;
    private readonly object _lock = new();
    
    private string? _currentToken;
    private DateTime _tokenExpiry = DateTime.MinValue;
    
    // Explicit JsonSerializerOptions
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public JwtAuthService(IHttpClientFactory httpClientFactory, ILogger<JwtAuthService> logger, IOptions<ApiOptions> api)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _api = api.Value;
    }

    public bool IsTokenValid
    {
        get
        {
            lock (_lock)
            {
                return !string.IsNullOrEmpty(_currentToken) && 
                       DateTime.UtcNow < _tokenExpiry.AddMinutes(-2); // 2 minute buffer
            }
        }
    }

    public async Task<string?> GetValidTokenAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (IsTokenValid)
            {
                return _currentToken;
            }
        }

        // Token is expired or null, refresh it
        var success = await RefreshTokenAsync(ct);
        if (!success)
        {
            return null;
        }

        lock (_lock)
        {
            return _currentToken;
        }
    }

    public async Task<bool> RefreshTokenAsync(CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_api.ApiKey))
            {
                _logger.LogError("Agent API key is not configured");
                return false;
            }
            if (!_api.TenantId.HasValue || _api.TenantId == Guid.Empty)
            {
                _logger.LogError("Agent tenantId is not configured or is empty");
                return false;
            }
            var requestPayload = new {
                AgentKey = _api.ApiKey
            };

            var payloadJson = JsonSerializer.Serialize(requestPayload, s_jsonOptions);
            _logger.LogInformation("Sending JWT auth request payload: {PayloadJson}", payloadJson);

            using var http = _httpClientFactory.CreateClient("JwtAuth");
            http.BaseAddress = new Uri(_api.BaseUrl);

            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/agents/auth")
            {
                Content = JsonContent.Create(requestPayload, options: s_jsonOptions)
            };

            using var res = await http.SendAsync(req, ct);

            var responseBody = await res.Content.ReadAsStringAsync(ct);
            _logger.LogInformation("JWT auth response: StatusCode={StatusCode}, Body={Body}", res.StatusCode, responseBody);

            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("JWT authentication failed: {StatusCode} - {ReasonPhrase}. Response: {ErrorBody}", 
                    res.StatusCode, res.ReasonPhrase, responseBody);
                return false;
            }

            AgentAuthResponse? response = null;
            try
            {
                response = JsonSerializer.Deserialize<AgentAuthResponse>(responseBody, s_jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during JWT auth response deserialization. Raw response: {RawResponse}", responseBody);
            }

            if (response == null || string.IsNullOrEmpty(response.Token))
            {
                _logger.LogWarning("JWT authentication response was null or missing token. Raw response: {RawResponse}", responseBody);
                return false;
            }

            lock (_lock)
            {
                _currentToken = response.Token;
                _tokenExpiry = response.ExpiresAt;
            }

            _logger.LogInformation("JWT token refreshed successfully, expires at {ExpiresAt:yyyy-MM-dd HH:mm:ss} UTC. Token: {Token}", 
                response.ExpiresAt, response.Token);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Top-level exception in RefreshTokenAsync");
            return false;
        }
    }
}