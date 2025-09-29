using System.ComponentModel.DataAnnotations;

namespace ProdControlAV.API.Models;

/// <summary>
/// Configuration settings for JWT token generation and validation
/// </summary>
public sealed class JwtConfig
{
    /// <summary>
    /// The issuer of the JWT tokens (typically the API endpoint)
    /// </summary>
    public string Issuer { get; set; } = string.Empty;
    
    /// <summary>
    /// The intended audience for the JWT tokens (typically the agents)
    /// </summary>
    public string Audience { get; set; } = string.Empty;
    
    /// <summary>
    /// The secret key used for signing JWT tokens
    /// Must be at least 32 characters for security
    /// </summary>
    [MinLength(32, ErrorMessage = "JWT key must be at least 32 characters for security")]
    public string Key { get; set; } = string.Empty;
    
    /// <summary>
    /// The expiry time for JWT tokens in minutes
    /// Defaults to 30 minutes for security
    /// </summary>
    [Range(1, 1440, ErrorMessage = "Expiry minutes must be between 1 and 1440 (24 hours)")]
    public int ExpiryMinutes { get; set; } = 30;
}