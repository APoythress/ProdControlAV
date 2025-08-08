using System.Threading.Tasks;

public interface IDeviceStatusRepository
{
    Task SaveStatusAsync(DeviceStatusLog status);
}