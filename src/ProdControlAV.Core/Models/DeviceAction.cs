using System;

namespace ProdControlAV.Core.Models;

public class DeviceAction
{
    public Guid DeviceId { get; set; }
    public Guid TenantId { get; set; }
    public Guid ActionId { get; set; }
    public string ActionName { get; set; }
    public string? Command { get; set; }
    public string HttpMethod { get; set; }
    public string? Response { get; set; }
}

public class Command
{
    public string CommandString { get; set; }
    public string Response { get; set; }
    public Guid DeviceId { get; set; } // bind the action to the device
    // the deviceId will be leveraged to generate the IP and the Port upon execution
}
