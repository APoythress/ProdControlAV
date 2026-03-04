using System.Collections.Concurrent;

namespace ProdControlAV.Agent.Services;

/// <summary>
/// Maintains one persistent <see cref="HyperDeckConnection"/> per device,
/// keyed by "{host}:{port}".
/// </summary>
public sealed class HyperDeckConnectionPool : IAsyncDisposable
{
    private readonly ILogger<HyperDeckConnectionPool> _logger;
    private readonly ConcurrentDictionary<string, HyperDeckConnection> _connections = new();
    private bool _disposed;

    public HyperDeckConnectionPool(ILogger<HyperDeckConnectionPool> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Returns the existing connection for <paramref name="host"/>:<paramref name="port"/>,
    /// or creates and starts a new one.
    /// </summary>
    public async Task<HyperDeckConnection> GetOrCreateAsync(
        string host, int port, CancellationToken ct = default)
    {
        var key = $"{host}:{port}";

        if (_connections.TryGetValue(key, out var existing))
            return existing;

        var connection = new HyperDeckConnection(host, port, _logger);
        if (_connections.TryAdd(key, connection))
        {
            _logger.LogInformation("Creating new HyperDeck connection for {Key}", key);
            await connection.StartAsync(ct);
            return connection;
        }

        // Another thread won the race – discard ours and return the winner
        await connection.DisposeAsync();
        return _connections[key];
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var connection in _connections.Values)
        {
            try { await connection.DisposeAsync(); }
            catch { /* best effort */ }
        }

        _connections.Clear();
    }
}
