using System;

namespace ProdControlAV.API.Models;

public class Tenant
{
    // Use string to align with current provider; value generated from Guid
    public Guid TenantId { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
