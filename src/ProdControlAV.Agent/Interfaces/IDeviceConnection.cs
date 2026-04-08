namespace ProdControlAV.Agent.Interfaces;

/// <summary>
/// Standard interface for all network-controlled device connections in the shared TCP framework.
/// Device-specific implementations inherit from <see cref="ProdControlAV.Agent.Services.BaseTcpDeviceConnection"/>.
/// </summary>
public interface IDeviceConnection
{
    /// <summary>
    /// Establishes the network connection and starts the background read/write loops.
    /// </summary>
    Task StartAsync(CancellationToken ct = default);
}
