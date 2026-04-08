namespace ProdControlAV.Agent.Services;

/// <summary>
/// Typed wrapper around <see cref="DeviceConnectionPool"/> that maintains one
/// persistent <see cref="HyperDeckConnection"/> per device, keyed by "{host}:{port}".
///
/// Delegates all lifecycle management to the shared <see cref="DeviceConnectionPool"/>
/// using the device-type label "hyperdeck".
/// </summary>
public sealed class HyperDeckConnectionPool : IAsyncDisposable
{
    private readonly ILogger<HyperDeckConnectionPool> _logger;
    private readonly DeviceConnectionPool _pool;
    private bool _disposed;

    public HyperDeckConnectionPool(ILogger<HyperDeckConnectionPool> logger)
    {
        _logger = logger;
        _pool = new DeviceConnectionPool(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DeviceConnectionPool>.Instance);
    }

    /// <summary>
    /// Returns the existing <see cref="HyperDeckConnection"/> for
    /// <paramref name="host"/>:<paramref name="port"/>, or creates and starts a new one.
    /// </summary>
    public async Task<HyperDeckConnection> GetOrCreateAsync(
        string host, int port, CancellationToken ct = default)
    {
        var connection = await _pool.GetOrCreateAsync(
            deviceType: "hyperdeck",
            host: host,
            port: port,
            factory: () => new HyperDeckConnection(host, port, _logger),
            ct: ct);

        return (HyperDeckConnection)connection;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _pool.DisposeAsync();
    }
}
