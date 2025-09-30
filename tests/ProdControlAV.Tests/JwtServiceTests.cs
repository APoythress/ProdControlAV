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
}