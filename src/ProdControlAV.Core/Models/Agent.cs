using System;
using System.ComponentModel.DataAnnotations;

namespace ProdControlAV.Core.Models;

public class Agent
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; } = default!;
    public string Name { get; set; } = "Agent";
    public string AgentKeyHash { get; set; } = default!;
    public string? LastHostname { get; set; }
    public string? LastIp { get; set; }
    public DateTime? LastSeenUtc { get; set; }
    public string? Version { get; set; }
    public string? LocationName { get; set; }
}