using System.Threading.Tasks;

namespace ProdControlAV.Core.Interfaces;

public interface INetworkMonitor
{
    Task<bool> IsDeviceOnlineAsync(string ipAddress);
}
