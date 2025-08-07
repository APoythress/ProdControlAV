namespace ProdControlAV.WebApp.Models
{
    public class DeviceStatusDto
    {
        public string Name { get; set; } = string.Empty;
        public string IP { get; set; } = string.Empty;
        public bool IsOnline { get; set; }
        public long LastPingMs { get; set; }
    }
}