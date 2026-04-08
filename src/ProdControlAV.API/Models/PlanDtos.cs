using ProdControlAV.Core.Models;

namespace ProdControlAV.API.Models;

/// <summary>
/// DTO for user subscription plan information
/// </summary>
public record UserPlanDto(
    SubscriptionPlan CurrentPlan,
    bool CanUpgrade,
    bool SmsNotificationsEnabled,
    string? MaskedPhoneNumber  // Shows only last 4 digits: ***-***-1234
);

/// <summary>
/// Request to upgrade user subscription plan
/// </summary>
public record UpgradePlanRequest(
    SubscriptionPlan NewPlan
);

/// <summary>
/// Request to update SMS notification preferences
/// </summary>
public record UpdateSmsPreferencesRequest(
    string? PhoneNumber,  // E.164 format: +15551234567
    bool SmsNotificationsEnabled
);
