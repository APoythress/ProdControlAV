using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using ProdControlAV.API.Auth;
using ProdControlAV.API.Controllers;
using ProdControlAV.API.Data;
using ProdControlAV.Core.Models;
using Xunit;

namespace ProdControlAV.Tests;

public class AdminControllerTests
{
    private readonly Guid _testTenantId = Guid.NewGuid();
    private readonly Guid _adminUserId = Guid.NewGuid();

    private AppDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var tenantProvider = new TestTenantProvider(_testTenantId);
        return new AppDbContext(options, tenantProvider);
    }

    [Fact]
    public async Task SeedCommandTemplates_CreatesNewTemplates()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var logger = new Mock<ILogger<AdminController>>();
        var controller = new AdminController(context, logger.Object);

        // Act
        var result = controller.SeedCommandTemplates();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = okResult.Value;
        
        // Verify templates were created
        var templates = context.CommandTemplates.ToList();
        Assert.NotEmpty(templates);
        
        // Verify response structure
        var responseType = response.GetType();
        var successProp = responseType.GetProperty("success");
        Assert.NotNull(successProp);
        Assert.True((bool)successProp.GetValue(response));
    }

    [Fact]
    public void SeedCommandTemplates_IsIdempotent()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var logger = new Mock<ILogger<AdminController>>();
        var controller = new AdminController(context, logger.Object);

        // Act - Seed twice
        controller.SeedCommandTemplates();
        var firstCount = context.CommandTemplates.Count();
        
        controller.SeedCommandTemplates();
        var secondCount = context.CommandTemplates.Count();

        // Assert - Count should be the same
        Assert.Equal(firstCount, secondCount);
    }

    [Fact]
    public void GetCommandTemplateStats_ReturnsCorrectStats()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var logger = new Mock<ILogger<AdminController>>();
        var controller = new AdminController(context, logger.Object);
        
        // Seed templates first
        CommandTemplateSeeder.SeedCommandTemplates(context);

        // Act
        var result = controller.GetCommandTemplateStats();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = okResult.Value;
        
        var responseType = response.GetType();
        var totalTemplatesProp = responseType.GetProperty("totalTemplates");
        var activeTemplatesProp = responseType.GetProperty("activeTemplates");
        
        Assert.NotNull(totalTemplatesProp);
        Assert.NotNull(activeTemplatesProp);
        
        var totalTemplates = (int)totalTemplatesProp.GetValue(response);
        var activeTemplates = (int)activeTemplatesProp.GetValue(response);
        
        Assert.True(totalTemplates > 0);
        Assert.Equal(totalTemplates, activeTemplates); // All seeded templates are active
    }

    [Fact]
    public void GetCommandTemplateStats_IncludesCategoryBreakdown()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var logger = new Mock<ILogger<AdminController>>();
        var controller = new AdminController(context, logger.Object);
        
        // Seed templates first
        CommandTemplateSeeder.SeedCommandTemplates(context);

        // Act
        var result = controller.GetCommandTemplateStats();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = okResult.Value;
        
        var responseType = response.GetType();
        var byCategoryProp = responseType.GetProperty("byCategory");
        
        Assert.NotNull(byCategoryProp);
        
        var byCategory = byCategoryProp.GetValue(response);
        Assert.NotNull(byCategory);
    }

    [Fact]
    public async Task AdminHandler_SucceedsForAdminUser()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        
        // Create admin user and tenant membership
        var user = new AppUser
        {
            UserId = _adminUserId,
            Email = "admin@test.com",
            PasswordHash = "hash",
            TenantId = _testTenantId
        };
        context.Users.Add(user);
        
        var userTenant = new UserTenant
        {
            UserId = _adminUserId,
            TenantId = _testTenantId,
            Role = "Admin"
        };
        context.UserTenants.Add(userTenant);
        await context.SaveChangesAsync();
        
        // Create claims principal
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, _adminUserId.ToString()),
            new Claim("tenant_id", _testTenantId.ToString())
        };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        
        var authContext = new AuthorizationHandlerContext(
            new[] { new AdminRequirement() },
            principal,
            null);
        
        var handler = new AdminHandler(context);

        // Act
        await handler.HandleAsync(authContext);

        // Assert
        Assert.True(authContext.HasSucceeded);
    }

    [Fact]
    public async Task AdminHandler_FailsForNonAdminUser()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        
        // Create regular user (non-admin)
        var user = new AppUser
        {
            UserId = _adminUserId,
            Email = "user@test.com",
            PasswordHash = "hash",
            TenantId = _testTenantId
        };
        context.Users.Add(user);
        
        var userTenant = new UserTenant
        {
            UserId = _adminUserId,
            TenantId = _testTenantId,
            Role = "Member" // Not Admin
        };
        context.UserTenants.Add(userTenant);
        await context.SaveChangesAsync();
        
        // Create claims principal
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, _adminUserId.ToString()),
            new Claim("tenant_id", _testTenantId.ToString())
        };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        
        var authContext = new AuthorizationHandlerContext(
            new[] { new AdminRequirement() },
            principal,
            null);
        
        var handler = new AdminHandler(context);

        // Act
        await handler.HandleAsync(authContext);

        // Assert
        Assert.False(authContext.HasSucceeded);
    }

    [Fact]
    public async Task AdminHandler_FailsWhenNoTenantClaim()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        
        // Create claims principal without tenant_id
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, _adminUserId.ToString())
            // No tenant_id claim
        };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        
        var authContext = new AuthorizationHandlerContext(
            new[] { new AdminRequirement() },
            principal,
            null);
        
        var handler = new AdminHandler(context);

        // Act
        await handler.HandleAsync(authContext);

        // Assert
        Assert.False(authContext.HasSucceeded);
    }

    private class TestTenantProvider : ITenantProvider
    {
        public Guid TenantId { get; }
        
        public TestTenantProvider(Guid tenantId)
        {
            TenantId = tenantId;
        }
    }
}
