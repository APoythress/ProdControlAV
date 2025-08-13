using ProdControlAV.Agent.Models;
using ProdControlAV.API.Controllers;

public interface ILocalDevicePoller
{
    Task<IReadOnlyList<AgentsController.StatusReading>> CollectAsync(IReadOnlyList<Device> devices, CancellationToken ct);
    Task<(bool Success, string Message)> ExecuteAsync(AgentsController.CommandEnvelope command, CancellationToken ct);
}