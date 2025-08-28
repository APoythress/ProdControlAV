using System;
using System.Threading;
using System.Threading.Tasks;

namespace ProdControlAV.API.Services
{
    public sealed record DeviceCommandResult(bool Success, int? StatusCode, string Message, string? ResponseBody);

    public interface IDeviceCommandService
    {
        Task<DeviceCommandResult> ExecuteDeviceActionAsync(Guid commandId, Guid userTenantId, CancellationToken ct = default);
    }
}