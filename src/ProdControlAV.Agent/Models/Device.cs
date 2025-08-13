namespace ProdControlAV.Agent.Models;

public sealed class Device
{
    public required string Id { get; init; }          // server identifier for the device
    public required string Name { get; init; }
    public required string Ip { get; init; }
    public bool PreferTcp { get; init; } = false;     // use TCP probe instead of ICMP when true
}
