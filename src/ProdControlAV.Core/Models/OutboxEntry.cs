using System;

namespace ProdControlAV.Core.Models;

public class OutboxEntry
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string EntityType { get; set; } = string.Empty; // "Device", "DeviceAction", etc.
    public Guid EntityId { get; set; }
    public string Operation { get; set; } = string.Empty; // "Upsert" or "Delete"
    public string? Payload { get; set; } // JSON serialized entity for Upsert operations
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? ProcessedUtc { get; set; }
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
}
