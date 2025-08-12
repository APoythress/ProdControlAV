using System;
using System.Collections.Generic;

namespace ProdControlAV.API.Models;

public class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = default!;
    public string? DisplayName { get; set; }

    public List<UserTenant> Memberships { get; set; } = new();
}
