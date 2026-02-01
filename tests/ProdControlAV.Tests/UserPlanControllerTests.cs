using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using ProdControlAV.API.Controllers;
using ProdControlAV.API.Models;
using ProdControlAV.Core.Interfaces;
using ProdControlAV.Core.Models;
using Xunit;

namespace ProdControlAV.Tests;

public class UserPlanControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Mock<ITenantProvider> _tenantProvider;
    private readonly Mock<IDataProtectionService> _dataProtection;
    private readonly Mock<ILogger<UserPlanController>> _logger;
    private readonly UserPlanController _controller;
    private readonly Guid _testTenantId = Guid.NewGuid();
    private readonly Guid _testUserId = Guid.NewGuid();

    public UserPlanControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _tenantProvider = new Mock<ITenantProvider>();
        _tenantProvider.Setup(x => x.TenantId).Returns(_testTenantId);

        _db = new AppDbContext(options, _tenantProvider.Object);
        
        _dataProtection = new Mock<IDataProtectionService>();
        _dataProtection.Setup(x => x.Protect(It.IsAny<string>())).Returns<string>(s => $"encrypted_{s}");
        _dataProtection.Setup(x => x.Unprotect(It.IsAny<string>())).Returns<string>(s => s.Replace("encrypted_", ""));
        
        _logger = new Mock<ILogger<UserPlanController>>();
        
        _controller = new UserPlanController(_db, _tenantProvider.Object, _dataProtection.Object, _logger.Object);

        // Set up test user
        var user = new AppUser
        {
            UserId = _testUserId,
            Email = "test@example.com",
            PasswordHash = "test_hash",
            TenantId = _testTenantId,
            SubscriptionPlan = SubscriptionPlan.Base
        };
        _db.Users.Add(user);
        _db.SaveChanges();

        // Mock HTTP context to provide user claims
        var mockHttpContext = new Mock<Microsoft.AspNetCore.Http.HttpContext>();
        var claims = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim("user_id", _testUserId.ToString())
            }, "test"));
        mockHttpContext.Setup(x => x.User).Returns(claims);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = mockHttpContext.Object
        };
    }

    [Fact]
    public async Task GetCurrentPlan_ReturnsBasePlan_ForNewUser()
    {
        // Act
        var result = await _controller.GetCurrentPlan();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var plan = Assert.IsType<UserPlanDto>(okResult.Value);
        Assert.Equal(SubscriptionPlan.Base, plan.CurrentPlan);
        Assert.True(plan.CanUpgrade);
        Assert.False(plan.SmsNotificationsEnabled);
        Assert.Null(plan.MaskedPhoneNumber);
    }

    [Fact]
    public async Task UpgradePlan_UpgradesFromBaseToPro()
    {
        // Arrange
        var request = new UpgradePlanRequest(SubscriptionPlan.Pro);

        // Act
        var result = await _controller.UpgradePlan(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var plan = Assert.IsType<UserPlanDto>(okResult.Value);
        Assert.Equal(SubscriptionPlan.Pro, plan.CurrentPlan);
        Assert.False(plan.CanUpgrade);

        // Verify database was updated
        var user = await _db.Users.FindAsync(_testUserId);
        Assert.NotNull(user);
        Assert.Equal(SubscriptionPlan.Pro, user!.SubscriptionPlan);
    }

    [Fact]
    public async Task UpgradePlan_WhenAlreadyPro_ReturnsBadRequest()
    {
        // Arrange - upgrade user to Pro first
        var user = await _db.Users.FindAsync(_testUserId);
        user!.SubscriptionPlan = SubscriptionPlan.Pro;
        await _db.SaveChangesAsync();

        var request = new UpgradePlanRequest(SubscriptionPlan.Pro);

        // Act
        var result = await _controller.UpgradePlan(request);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task UpdateSmsPreferences_RequiresProPlan()
    {
        // Arrange
        var request = new UpdateSmsPreferencesRequest("+15551234567", true);

        // Act
        var result = await _controller.UpdateSmsPreferences(request);

        // Assert
        var badResult = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("Pro plan", badResult.Value!.ToString());
    }

    [Fact]
    public async Task UpdateSmsPreferences_WithProPlan_SavesPhoneNumberEncrypted()
    {
        // Arrange - upgrade user to Pro first
        var user = await _db.Users.FindAsync(_testUserId);
        user!.SubscriptionPlan = SubscriptionPlan.Pro;
        await _db.SaveChangesAsync();

        var request = new UpdateSmsPreferencesRequest("+15551234567", true);

        // Act
        var result = await _controller.UpdateSmsPreferences(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var plan = Assert.IsType<UserPlanDto>(okResult.Value);
        Assert.True(plan.SmsNotificationsEnabled);
        Assert.NotNull(plan.MaskedPhoneNumber);

        // Verify phone number was encrypted in database
        user = await _db.Users.FindAsync(_testUserId);
        Assert.NotNull(user!.PhoneNumber);
        Assert.StartsWith("encrypted_", user.PhoneNumber);
        Assert.True(user.SmsNotificationsEnabled);

        // Verify encryption was called
        _dataProtection.Verify(x => x.Protect("+15551234567"), Times.Once);
    }

    [Fact]
    public async Task UpdateSmsPreferences_WithInvalidPhoneFormat_ReturnsBadRequest()
    {
        // Arrange - upgrade user to Pro first
        var user = await _db.Users.FindAsync(_testUserId);
        user!.SubscriptionPlan = SubscriptionPlan.Pro;
        await _db.SaveChangesAsync();

        var request = new UpdateSmsPreferencesRequest("1234567890", true); // Missing + prefix

        // Act
        var result = await _controller.UpdateSmsPreferences(request);

        // Assert
        var badResult = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("E.164 format", badResult.Value!.ToString());
    }

    [Fact]
    public async Task UpdateSmsPreferences_DisablingSms_KeepsPhoneNumber()
    {
        // Arrange - set up user with Pro plan and SMS enabled
        var user = await _db.Users.FindAsync(_testUserId);
        user!.SubscriptionPlan = SubscriptionPlan.Pro;
        user.PhoneNumber = "encrypted_+15551234567";
        user.SmsNotificationsEnabled = true;
        await _db.SaveChangesAsync();

        var request = new UpdateSmsPreferencesRequest(null, false);

        // Act
        var result = await _controller.UpdateSmsPreferences(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var plan = Assert.IsType<UserPlanDto>(okResult.Value);
        Assert.False(plan.SmsNotificationsEnabled);
        
        // Phone number should still be stored (for re-enabling)
        user = await _db.Users.FindAsync(_testUserId);
        Assert.NotNull(user!.PhoneNumber);
        Assert.False(user.SmsNotificationsEnabled);
    }

    public void Dispose()
    {
        _db.Dispose();
    }
}
