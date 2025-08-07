namespace ProdControlAV.Core.Models;

public class CommandResult
{
    public string DeviceId { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Response { get; set; }
}
