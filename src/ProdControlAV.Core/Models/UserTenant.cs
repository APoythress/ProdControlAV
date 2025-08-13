using System;

namespace ProdControlAV.API.Models;

public class UserTenant
{
    public Guid UserId { get; set; }
    public string TenantId { get; set; } = default!;
    public string Role { get; set; } = "Member";

    public AppUser User { get; set; } = default!;
    public Tenant Tenant { get; set; } = default!;
}
