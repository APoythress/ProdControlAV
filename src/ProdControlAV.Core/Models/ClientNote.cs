using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProdControlAV.Core.Models;

[Table("ClientNotes")]
public class ClientNote
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    
    [Required]
    public Guid TenantId { get; set; }
    
    [Required]
    [MaxLength(500)]
    public string NoteText { get; set; } = default!;
    
    [Required]
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    
    [Required]
    [MaxLength(200)]
    public string CreatedBy { get; set; } = default!;
}
