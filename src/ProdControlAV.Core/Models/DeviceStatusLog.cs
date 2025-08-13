using System;

public class DeviceStatusLog
{
    public Guid Id { get; set; }
    public string DeviceName { get; set; }
    public string IP { get; set; }
    public bool IsOnline { get; set; }
    public long LastPingMs { get; set; }
    public DateTime Timestamp { get; set; }
}