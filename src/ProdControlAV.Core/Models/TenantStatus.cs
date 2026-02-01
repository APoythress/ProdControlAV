using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProdControlAV.Core.Models;

[Table("TenantStatus")]
public class TenantStatus
{
    [Key]
    public int TenantStatusId { get; set; }

    [Required]
    [MaxLength(25)]
    public string TenantStatusText { get; set; } = default!;
}
