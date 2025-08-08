namespace ProdControlAV.Core.Models;

public class DeviceAction
{
    public string DeviceId { get; set; }
    public string Label { get; set; }
    public string? Command { get; set; }
    public bool Success { get; set; }
    public string? Response { get; set; }
}
