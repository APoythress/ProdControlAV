using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProdControlAV.Core.Models;

// Note: Table name is 'SubscriptionPlans' (as per DB schema design), but class is named
// 'TenantSubscriptionPlan' (singular) to avoid naming conflict with the existing
// SubscriptionPlan enum in AppUser.cs and follow C# naming conventions for entity classes.
[Table("SubscriptionPlans")]
public class TenantSubscriptionPlan
{
    [Key]
    public int SubscriptionPlanId { get; set; }

    [Required]
    [MaxLength(25)]
    public string SubscriptionPlanText { get; set; } = default!;
}
