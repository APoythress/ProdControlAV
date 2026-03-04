using System.Collections.Concurrent;
using ProdControlAV.Agent.Interfaces;

namespace ProdControlAV.Agent.Services;

/// <summary>
/// Maintains one <see cref="IDeviceConnection"/> per unique device endpoint,
/// keyed by <c>{deviceType}:{host}:{port}</c>.
///
/// Connections are created on demand via a caller-supplied factory and reused
/// for the lifetime of the pool.  This is the central registry used by the
/// command router to obtain persistent device connections without duplicating
/// connection management logic across device types.
/// </summary>
public sealed class DeviceConnectionPool : IAsyncDisposable
{
    private readonly ILogger<DeviceConnectionPool> _logger;
    private readonly ConcurrentDictionary<string, IDeviceConnection> _connections = new();
    private bool _disposed;

    public DeviceConnectionPool(ILogger<DeviceConnectionPool> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Returns the existing connection for the given endpoint key, or creates and
    /// starts a new one using <paramref name="factory"/>.
    /// </summary>
    /// <param name="deviceType">Logical device type label (e.g. "hyperdeck", "atem").</param>
    /// <param name="host">Device IP address or hostname.</param>
    /// <param name="port">Device TCP port.</param>
    /// <param name="factory">
    /// Factory invoked once to create the connection instance.  Must not return null.
    /// </param>
    /// <param name="ct">Cancellation token forwarded to <see cref="IDeviceConnection.StartAsync"/>.</param>
    public async Task<IDeviceConnection> GetOrCreateAsync(
        string deviceType,
        string host,
        int port,
        Func<IDeviceConnection> factory,
        CancellationToken ct = default)
    {
        var key = BuildKey(deviceType, host, port);

        if (_connections.TryGetValue(key, out var existing))
            return existing;

        var connection = factory();
        if (_connections.TryAdd(key, connection))
        {
            _logger.LogInformation(
                "Creating new {DeviceType} connection for {Key}", deviceType, key);
            await connection.StartAsync(ct);
            return connection;
        }

        // Another thread won the race – dispose ours and return the winner.
        if (connection is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
        else if (connection is IDisposable disposable)
            disposable.Dispose();

        return _connections[key];
    }

    /// <summary>Removes and disposes the connection for the given endpoint, if present.</summary>
    public async Task RemoveAsync(string deviceType, string host, int port)
    {
        var key = BuildKey(deviceType, host, port);
        if (_connections.TryRemove(key, out var connection))
        {
            if (connection is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
            else if (connection is IDisposable disposable)
                disposable.Dispose();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var connection in _connections.Values)
        {
            try
            {
                if (connection is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync();
                else if (connection is IDisposable disposable)
                    disposable.Dispose();
            }
            catch { /* best effort */ }
        }

        _connections.Clear();
    }

    private static string BuildKey(string deviceType, string host, int port)
        => $"{deviceType.ToLowerInvariant()}:{host}:{port}";
}
