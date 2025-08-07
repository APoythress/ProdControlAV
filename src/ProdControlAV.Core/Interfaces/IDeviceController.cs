using System.Threading.Tasks;

namespace ProdControlAV.Core.Interfaces;

public interface IDeviceController
{
    Task<bool> SendCommandAsync(string deviceId, string command);
    Task<string?> GetStatusAsync(string deviceId);
}
