using System.ComponentModel.DataAnnotations;

namespace ProdControlAV.API.Models;

/// <summary>
/// Request model for agent authentication
/// </summary>
public sealed class AgentAuthRequest
{
    /// <summary>
    /// The agent's secret key for authentication
    /// </summary>
    [Required(ErrorMessage = "Agent key is required")]
    [MinLength(1, ErrorMessage = "Agent key cannot be empty")]
    public string AgentKey { get; set; } = string.Empty;
}

/// <summary>
/// Response model for successful agent authentication
/// </summary>
public sealed class AgentAuthResponse
{
    /// <summary>
    /// The JWT token for subsequent API requests
    /// </summary>
    public string Token { get; set; } = string.Empty;
    
    /// <summary>
    /// When the token expires (UTC)
    /// </summary>
    public DateTime ExpiresAt { get; set; }
    
    /// <summary>
    /// The token type (always "Bearer")
    /// </summary>
    public string TokenType { get; set; } = "Bearer";
}