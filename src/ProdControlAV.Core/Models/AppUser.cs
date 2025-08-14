using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProdControlAV.API.Models;

public class AppUser
{
    public Guid UserId { get; set; } = Guid.NewGuid();
    
    public Guid TenantId { get; set; }
    public string Email { get; set; } = default!;
    public string? DisplayName { get; set; }
    public List<UserTenant> Memberships { get; set; } = new();
    public string PasswordHash { get; set; }
}
