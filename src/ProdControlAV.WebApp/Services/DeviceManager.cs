using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using ProdControlAV.Core.Models;
using ProdControlAV.WebApp.Models;

namespace ProdControlAV.Infrastructure.Services
{
    public class DeviceManager
    {
        private readonly List<DeviceStatusDto> _devices = new()
        {
            new() { Name = "Behringer Wing", IP = "192.168.1.101" },
            new() { Name = "HyperDeck", IP = "192.168.1.102" },
            new() { Name = "Video Switcher", IP = "192.168.1.103" }
        };

        public List<DeviceStatusDto> GetAllDevices() => _devices;

        public async Task<long> PingDeviceAsync(string ip)
        {
            var ping = new Ping();
            var reply = await ping.SendPingAsync(ip, 1000);
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : -1;
        }

        public async Task SendCommandAsync(string ip, string command)
        {
            // Placeholder logic for telnet
            using var client = new TcpClient();
            await client.ConnectAsync(ip, 23);
            using var stream = client.GetStream();
            var writer = new StreamWriter(stream) { AutoFlush = true };
            await writer.WriteLineAsync(command);
        }
    }
}