using System.Collections.Generic;
using System.Threading.Tasks;

public class InMemoryDeviceStatusRepository : IDeviceStatusRepository
{
    private readonly List<DeviceStatusLog> _logs = new();

    public Task SaveStatusAsync(DeviceStatusLog status)
    {
        _logs.Add(status);
        return Task.CompletedTask;
    }
}