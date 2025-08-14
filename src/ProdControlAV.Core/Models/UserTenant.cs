using System;

namespace ProdControlAV.API.Models;

public class UserTenant
{
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public string Role { get; set; }
}
