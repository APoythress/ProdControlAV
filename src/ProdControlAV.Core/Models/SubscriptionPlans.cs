using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProdControlAV.Core.Models;

[Table("SubscriptionPlans")]
public class SubscriptionPlans
{
    [Key]
    public int SubscriptionPlanId { get; set; }

    [Required]
    [MaxLength(25)]
    public string SubscriptionPlanText { get; set; } = default!;
}
