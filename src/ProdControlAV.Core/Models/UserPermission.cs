using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProdControlAV.Core.Models;

/// <summary>
/// Represents a permission assigned to a user.
/// Users can have multiple permissions to control access to different features.
/// </summary>
[Table("UserPermissions")]
public class UserPermission
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public Guid UserId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Permission { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation property
    public AppUser? User { get; set; }
}
