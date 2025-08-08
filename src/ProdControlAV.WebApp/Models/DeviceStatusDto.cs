namespace ProdControlAV.WebApp.Models
{
    public class DeviceStatusDto
    {
        public string? Name { get; set; }
        public string? IP { get; set; }
        public bool IsOnline { get; set; }
        public long LastPingMs { get; set; }
    }
}