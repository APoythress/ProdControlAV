using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProdControlAV.Core.Models;

public enum SubscriptionPlan
{
    Base = 0,
    Pro = 1
}

[Table("Users")]
public class AppUser
{
    [Key]
    [Column("UserId")] // map to Users.Id
    public Guid UserId { get; set; }

    [Required]
    public string Email { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    [Required]
    public string PasswordHash { get; set; } = string.Empty;
    
    public Guid TenantId { get; set; }

    // Subscription plan management
    public SubscriptionPlan SubscriptionPlan { get; set; } = SubscriptionPlan.Base;
    
    // SMS notification settings (Pro plan only)
    public string? PhoneNumber { get; set; } // Encrypted phone number
    public bool SmsNotificationsEnabled { get; set; } = false;

    // Needed for UserTenantConfig: WithMany(u => u.Memberships)
    public ICollection<UserTenant> Memberships { get; set; } = new List<UserTenant>();
    
    // User permissions
    public ICollection<UserPermission> Permissions { get; set; } = new List<UserPermission>();
}