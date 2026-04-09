using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProdControlAV.API.Models;
using ProdControlAV.Core.Interfaces;
using ProdControlAV.Core.Models;

namespace ProdControlAV.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "TenantMember")]
public class UserPlanController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantProvider _tenant;
    private readonly IDataProtectionService _dataProtection;
    private readonly ILogger<UserPlanController> _logger;

    public UserPlanController(
        AppDbContext db,
        ITenantProvider tenant,
        IDataProtectionService dataProtection,
        ILogger<UserPlanController> logger)
    {
        _db = db;
        _tenant = tenant;
        _dataProtection = dataProtection;
        _logger = logger;
    }

    /// <summary>
    /// Get current user's subscription plan and SMS notification settings
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<UserPlanDto>> GetCurrentPlan()
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var user = await _db.Users.FindAsync(userId);
        if (user == null)
        {
            return NotFound();
        }

        var maskedPhone = string.IsNullOrEmpty(user.PhoneNumber)
            ? null
            : MaskPhoneNumber(DecryptPhoneNumber(user.PhoneNumber));

        var dto = new UserPlanDto(
            user.SubscriptionPlan,
            user.SubscriptionPlan == SubscriptionPlan.Base, // Can upgrade if on Base plan
            user.SmsNotificationsEnabled,
            maskedPhone
        );

        return Ok(dto);
    }

    /// <summary>
    /// Upgrade user subscription plan
    /// </summary>
    [HttpPost("upgrade")]
    public async Task<ActionResult<UserPlanDto>> UpgradePlan([FromBody] UpgradePlanRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var user = await _db.Users.FindAsync(userId);
        if (user == null)
        {
            return NotFound();
        }

        // Validate upgrade path (can only upgrade from Base to Pro)
        if (user.SubscriptionPlan == SubscriptionPlan.Pro && request.NewPlan == SubscriptionPlan.Pro)
        {
            return BadRequest("User is already on Pro plan");
        }

        if (request.NewPlan != SubscriptionPlan.Pro)
        {
            return BadRequest("Can only upgrade to Pro plan");
        }

        user.SubscriptionPlan = request.NewPlan;
        await _db.SaveChangesAsync();

        _logger.LogInformation("User {UserId} upgraded to {Plan} plan", userId, request.NewPlan);

        return await GetCurrentPlan();
    }

    /// <summary>
    /// Update SMS notification preferences (Pro plan only)
    /// </summary>
    [HttpPost("sms-preferences")]
    public async Task<ActionResult<UserPlanDto>> UpdateSmsPreferences([FromBody] UpdateSmsPreferencesRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var user = await _db.Users.FindAsync(userId);
        if (user == null)
        {
            return NotFound();
        }

        // Only Pro users can enable SMS notifications
        if (request.SmsNotificationsEnabled && user.SubscriptionPlan != SubscriptionPlan.Pro)
        {
            return BadRequest("SMS notifications require Pro plan. Please upgrade first.");
        }

        // Validate phone number format if enabling SMS
        if (request.SmsNotificationsEnabled)
        {
            if (string.IsNullOrEmpty(request.PhoneNumber))
            {
                return BadRequest("Phone number is required to enable SMS notifications");
            }

            if (!IsValidPhoneNumber(request.PhoneNumber))
            {
                return BadRequest("Invalid phone number format. Please use E.164 format (e.g., +15551234567)");
            }

            // Encrypt and store phone number
            user.PhoneNumber = EncryptPhoneNumber(request.PhoneNumber);
        }
        else
        {
            // If disabling, optionally clear phone number (or keep it for re-enabling)
            // For now, we'll keep it
        }

        user.SmsNotificationsEnabled = request.SmsNotificationsEnabled;
        await _db.SaveChangesAsync();

        _logger.LogInformation("User {UserId} updated SMS preferences: Enabled={Enabled}",
            userId, request.SmsNotificationsEnabled);

        return await GetCurrentPlan();
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst("user_id")?.Value ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
    }

    private string EncryptPhoneNumber(string phoneNumber)
    {
        return _dataProtection.Protect(phoneNumber);
    }

    private string DecryptPhoneNumber(string encryptedPhoneNumber)
    {
        try
        {
            return _dataProtection.Unprotect(encryptedPhoneNumber);
        }
        catch
        {
            _logger.LogWarning("Failed to decrypt phone number");
            return string.Empty;
        }
    }

    private static string MaskPhoneNumber(string phoneNumber)
    {
        if (string.IsNullOrEmpty(phoneNumber) || phoneNumber.Length < 4)
        {
            return "***";
        }

        return "***-***-" + phoneNumber.Substring(phoneNumber.Length - 4);
    }

    /// <summary>
    /// Validate E.164 phone number format (+[country code][number])
    /// </summary>
    private static bool IsValidPhoneNumber(string phoneNumber)
    {
        if (string.IsNullOrEmpty(phoneNumber))
        {
            return false;
        }

        // E.164 format: + followed by 1-15 digits
        var e164Regex = new Regex(@"^\+[1-9]\d{1,14}$");
        return e164Regex.IsMatch(phoneNumber);
    }
}
