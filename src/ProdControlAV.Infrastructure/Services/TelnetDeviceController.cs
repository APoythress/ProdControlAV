using ProdControlAV.Core.Interfaces;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace ProdControlAV.Infrastructure.Services;

public class TelnetDeviceController : IDeviceController
{
    private readonly int _port;

    public TelnetDeviceController(int port = 23)
    {
        _port = port;
    }

    public async Task<bool> SendCommandAsync(string deviceId, string command)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(deviceId, _port);
            using var stream = client.GetStream();
            byte[] buffer = Encoding.ASCII.GetBytes(command + "\r\n");
            await stream.WriteAsync(buffer, 0, buffer.Length);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string?> GetStatusAsync(string deviceId)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(deviceId, _port);
            using var stream = client.GetStream();
            byte[] buffer = Encoding.ASCII.GetBytes("status\r\n");
            await stream.WriteAsync(buffer, 0, buffer.Length);

            byte[] response = new byte[1024];
            int bytesRead = await stream.ReadAsync(response, 0, response.Length);
            return Encoding.ASCII.GetString(response, 0, bytesRead);
        }
        catch
        {
            return null;
        }
    }
}
