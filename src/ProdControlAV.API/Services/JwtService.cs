using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ProdControlAV.API.Models;
using ProdControlAV.Core.Models;

namespace ProdControlAV.API.Services;

/// <summary>
/// Interface for JWT token operations
/// </summary>
public interface IJwtService
{
    /// <summary>
    /// Generate a JWT token for an authenticated agent
    /// </summary>
    /// <param name="agent">The agent to generate a token for</param>
    /// <returns>The JWT token string and expiry time</returns>
    (string token, DateTime expiresAt) GenerateToken(Agent agent);
}

/// <summary>
/// Service for generating and validating JWT tokens for agents
/// </summary>
public sealed class JwtService : IJwtService
{
    private readonly JwtConfig _config;
    private readonly JwtSecurityTokenHandler _tokenHandler;
    private readonly SymmetricSecurityKey _signingKey;

    public JwtService(IOptions<JwtConfig> config)
    {
        _config = config.Value;
        _tokenHandler = new JwtSecurityTokenHandler();
        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config.Key));
    }

    public (string token, DateTime expiresAt) GenerateToken(Agent agent)
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(_config.ExpiryMinutes);
        
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, agent.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new Claim("tenantId", agent.TenantId.ToString()),
            new Claim("agentName", agent.Name ?? "Unknown"),
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expiresAt,
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256Signature),
            Issuer = _config.Issuer,
            Audience = _config.Audience,
            NotBefore = DateTime.UtcNow
        };

        var token = _tokenHandler.CreateToken(tokenDescriptor);
        var tokenString = _tokenHandler.WriteToken(token);

        return (tokenString, expiresAt);
    }
}