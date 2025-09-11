using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ProdControlAV.Core.Models;
using AgentDevice = ProdControlAV.Agent.Models.Device;

public interface ILocalDevicePoller
{
    Task<IReadOnlyList<StatusReading>> CollectAsync(IReadOnlyList<AgentDevice> devices, CancellationToken ct);
    Task<(bool Success, string Message)> ExecuteAsync(CommandEnvelope command, CancellationToken ct);
}