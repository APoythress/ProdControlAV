using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProdControlAV.Core.Models;

public class Tenant
{
    [Key]
    [Column("Id")] // map to Tenants.Id
    public Guid TenantId { get; set; }

    [Required]
    public string Name { get; set; } = default!;

    [Required]
    public string Slug { get; set; } = default!;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}