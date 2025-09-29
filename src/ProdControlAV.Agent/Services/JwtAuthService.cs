using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ProdControlAV.Agent.Services;

/// <summary>
/// Request model for agent JWT authentication
/// </summary>
public sealed class AgentAuthRequest
{
    public string AgentKey { get; set; } = string.Empty;
}

/// <summary>
/// Response model for JWT authentication
/// </summary>
public sealed class AgentAuthResponse
{
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
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

            var request = new AgentAuthRequest
            {
                AgentKey = _api.ApiKey
            };

            using var http = _httpClientFactory.CreateClient();
            http.BaseAddress = new Uri(_api.BaseUrl);

            using var req = new HttpRequestMessage(HttpMethod.Post, "/agents/auth")
            {
                Content = JsonContent.Create(request, options: s_jsonOptions)
            };

            using var res = await http.SendAsync(req, ct);
            
            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("JWT authentication failed: {StatusCode} - {ReasonPhrase}", 
                    res.StatusCode, res.ReasonPhrase);
                return false;
            }

            var response = await res.Content.ReadFromJsonAsync<AgentAuthResponse>(s_jsonOptions, ct);
            if (response == null || string.IsNullOrEmpty(response.Token))
            {
                _logger.LogWarning("JWT authentication response was null or empty");
                return false;
            }

            lock (_lock)
            {
                _currentToken = response.Token;
                _tokenExpiry = response.ExpiresAt;
            }

            _logger.LogInformation("JWT token refreshed successfully, expires at {ExpiresAt:yyyy-MM-dd HH:mm:ss} UTC", 
                response.ExpiresAt);
            
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("JWT token refresh was cancelled");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh JWT token");
            return false;
        }
    }
}