using System;

namespace ProdControlAV.Core.Models;

public class DeviceStatus
{
    public string DeviceId { get; set; }
    public string Name { get; set; }
    public string Status { get; set; }
    public DateTime LastChecked { get; set; }
    public string? LastResponse { get; set; }
}
