using ProdControlAV.Agent.Models;

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

    /// <summary>
    /// Sends a text command to the device and awaits the parsed response.
    /// Only one command may be in flight at a time; additional callers are serialised.
    /// </summary>
    /// <param name="command">Protocol command string (device-specific formatting applied by the implementation).</param>
    /// <param name="timeout">Maximum time to wait for a response.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="TimeoutException">Thrown when no response is received within <paramref name="timeout"/>.</exception>
    Task<DeviceResponse> SendCommandAsync(string command, TimeSpan timeout, CancellationToken ct = default);

    /// <summary>True when a live network connection to the device is established.</summary>
    bool IsConnected { get; }

    /// <summary>Gracefully tears down the network connection.</summary>
    Task DisconnectAsync();
}
