using System;

namespace ProdControlAV.Core.Models;

public class DeviceAction
{
    public Guid DeviceId { get; set; }
    public Guid TenantId { get; set; }
    public string ActionName { get; set; }
    public string? Command { get; set; }
    public string? Response { get; set; }
}
