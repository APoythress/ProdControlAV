using System;

namespace ProdControlAV.Core.Models;

public class DeviceStatus
{
    public string DeviceId { get; set; } = string.Empty;
    public bool IsOnline { get; set; }
    public DateTime LastChecked { get; set; }
    public string LastResponse { get; set; } = string.Empty;
}
