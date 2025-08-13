public interface ILocalDevicePoller
{
    Task<IReadOnlyList<StatusReading>> CollectAsync(IReadOnlyList<DeviceTarget> devices, CancellationToken ct);
    Task<(bool Success, string Message)> ExecuteAsync(CommandEnvelope command, CancellationToken ct);
}