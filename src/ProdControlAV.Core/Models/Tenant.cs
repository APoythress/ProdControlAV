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

    // Foreign keys for new relationships
    public int? TenantStatusId { get; set; }
    public int? SubscriptionPlanId { get; set; }

    // Navigation properties
    [ForeignKey("TenantStatusId")]
    public TenantStatus? TenantStatus { get; set; }

    [ForeignKey("SubscriptionPlanId")]
    public SubscriptionPlans? SubscriptionPlan { get; set; }
}