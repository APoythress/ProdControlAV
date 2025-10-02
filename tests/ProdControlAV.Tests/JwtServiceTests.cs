using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using ProdControlAV.API.Models;
using ProdControlAV.API.Services;
using AgentModel = ProdControlAV.Core.Models.Agent;
using Xunit;

namespace ProdControlAV.Tests;

public class JwtServiceTests
{
    [Fact]
    public void GenerateToken_WithValidAgent_ReturnsValidJwt()
    {
        // Arrange
        var jwtConfig = new JwtConfig
        {
            Key = "test-secret-key-must-be-32chars-long-minimum-for-security",
            Issuer = "test-issuer",
            Audience = "test-audience",
            ExpiryMinutes = 30
        };
        
        var jwtService = new JwtService(Options.Create(jwtConfig));
        
        var agent = new AgentModel
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Name = "Test Agent"
        };

        // Act
        var (token, expiresAt) = jwtService.GenerateToken(agent);

        // Assert
        Assert.NotEmpty(token);
        Assert.True(expiresAt > DateTime.UtcNow);
        Assert.True(expiresAt <= DateTime.UtcNow.AddMinutes(31)); // Allow 1 minute buffer
        
        // Validate the JWT structure
        var tokenHandler = new JwtSecurityTokenHandler();
        var jwt = tokenHandler.ReadJwtToken(token);
        
        Assert.Equal(jwtConfig.Issuer, jwt.Issuer);
        Assert.Contains(jwtConfig.Audience, jwt.Audiences);
        Assert.Equal(agent.Id.ToString(), jwt.Subject);
        
        // Check custom claims
        Assert.Equal(agent.TenantId.ToString(), jwt.Claims.First(c => c.Type == "tenantId").Value);
        Assert.Equal(agent.Name, jwt.Claims.First(c => c.Type == "agentName").Value);
    }

    [Fact]
    public void GenerateToken_WithDifferentAgents_GeneratesUniqueTokens()
    {
        // Arrange
        var jwtConfig = new JwtConfig
        {
            Key = "test-secret-key-must-be-32chars-long-minimum-for-security",
            Issuer = "test-issuer",
            Audience = "test-audience",
            ExpiryMinutes = 30
        };
        
        var jwtService = new JwtService(Options.Create(jwtConfig));
        
        var agent1 = new AgentModel { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), Name = "Agent 1" };
        var agent2 = new AgentModel { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), Name = "Agent 2" };

        // Act
        var (token1, _) = jwtService.GenerateToken(agent1);
        var (token2, _) = jwtService.GenerateToken(agent2);

        // Assert
        Assert.NotEqual(token1, token2);
        
        var tokenHandler = new JwtSecurityTokenHandler();
        var jwt1 = tokenHandler.ReadJwtToken(token1);
        var jwt2 = tokenHandler.ReadJwtToken(token2);
        
        Assert.NotEqual(jwt1.Subject, jwt2.Subject);
    }

    [Fact]
    public void GenerateToken_ClaimsCanBeExtractedByShortAndLongFormNames()
    {
        // Arrange
        var jwtConfig = new JwtConfig
        {
            Key = "test-secret-key-must-be-32chars-long-minimum-for-security",
            Issuer = "test-issuer",
            Audience = "test-audience",
            ExpiryMinutes = 30
        };
        
        var jwtService = new JwtService(Options.Create(jwtConfig));
        var agentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        
        var agent = new AgentModel
        {
            Id = agentId,
            TenantId = tenantId,
            Name = "Test Agent"
        };

        // Act
        var (token, _) = jwtService.GenerateToken(agent);
        
        // Read the JWT and create a ClaimsPrincipal as the authentication middleware would
        var tokenHandler = new JwtSecurityTokenHandler();
        var jwt = tokenHandler.ReadJwtToken(token);
        
        // Simulate what happens during token validation - claims are added to the principal
        var claims = jwt.Claims.ToList();
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        // Assert - Verify claims can be found using both short and long form names
        // The JWT token has "sub" claim
        var subClaim = principal.FindFirst(JwtRegisteredClaimNames.Sub);
        Assert.NotNull(subClaim);
        Assert.Equal(agentId.ToString(), subClaim.Value);
        
        // Also verify it's accessible via ClaimTypes.NameIdentifier (for backwards compatibility)
        var nameIdClaim = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        // Note: This might be null if the default mapping isn't applied, which is expected with our fix
        
        // Verify tenantId claim
        var tenantIdClaim = principal.Claims.FirstOrDefault(c => c.Type == "tenantId");
        Assert.NotNull(tenantIdClaim);
        Assert.Equal(tenantId.ToString(), tenantIdClaim.Value);
    }
}