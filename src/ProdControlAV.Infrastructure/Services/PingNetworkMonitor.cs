using ProdControlAV.Core.Interfaces;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace ProdControlAV.Infrastructure.Services;

public class PingNetworkMonitor : INetworkMonitor
{
    public async Task<bool> IsDeviceOnlineAsync(string ipAddress)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ipAddress, 1000);
            return reply.Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }
}
