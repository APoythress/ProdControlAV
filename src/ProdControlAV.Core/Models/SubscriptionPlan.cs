using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProdControlAV.Core.Models;

[Table("SubscriptionPlans")]
public class TenantSubscriptionPlan
{
    [Key]
    public int SubscriptionPlanId { get; set; }

    [Required]
    [MaxLength(25)]
    public string SubscriptionPlanText { get; set; } = default!;
}
