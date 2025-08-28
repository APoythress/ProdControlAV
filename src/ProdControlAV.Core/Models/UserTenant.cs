using System;
using System.ComponentModel.DataAnnotations.Schema;
using ProdControlAV.Core.Models; // Tenant lives here

namespace ProdControlAV.API.Models;

[Table("UserTenants")]
public class UserTenant
{
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public string Role { get; set; } = "Member";

    // Navigations
    public AppUser User { get; set; } = default!;
    public Tenant Tenant { get; set; } = default!;
}