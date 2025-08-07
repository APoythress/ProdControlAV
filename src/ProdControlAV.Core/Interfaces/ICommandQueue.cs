using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProdControlAV.Core.Interfaces;

public interface ICommandQueue
{
    Task<IEnumerable<string>> FetchPendingCommandsAsync(string deviceId);
}
