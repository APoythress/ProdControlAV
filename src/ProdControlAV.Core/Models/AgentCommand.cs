using System;
using System.ComponentModel.DataAnnotations;

namespace ProdControlAV.Core.Models;

public class AgentCommand
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = default!;
    public Guid AgentId { get; set; } = default!;
    public Guid DeviceId { get; set; } = default!;
    public string Verb { get; set; } = default!;
    public string? Payload { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? DueUtc { get; set; }
    public DateTime? TakenUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public bool? Success { get; set; }
    public string? Message { get; set; }
}