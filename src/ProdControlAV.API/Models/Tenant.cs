using System;

namespace ProdControlAV.API.Models;

public class Tenant
{
    // Use string to align with current provider; value generated from Guid
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Slug { get; set; } = default!;
    public string Name { get; set; } = default!;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
