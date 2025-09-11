using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProdControlAV.Core.Models;

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

    // Needed for UserTenantConfig: WithMany(u => u.Memberships)
    public ICollection<UserTenant> Memberships { get; set; } = new List<UserTenant>();
}