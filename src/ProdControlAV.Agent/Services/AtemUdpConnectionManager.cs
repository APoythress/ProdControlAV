using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ProdControlAV.Agent.Interfaces;

namespace ProdControlAV.Agent.Services;

public sealed class AtemUdpConnectionManager : IAsyncDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ConcurrentDictionary<Guid, AtemUdpConnection> _connections = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public AtemUdpConnectionManager(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public async Task<AtemUdpConnection> GetOrCreateAsync(
        Guid deviceId,
        string host,
        int port,
        CancellationToken ct)
    {
        if (_connections.TryGetValue(deviceId, out var existing))
            return existing;

        await _lock.WaitAsync(ct);
        try
        {
            if (_connections.TryGetValue(deviceId, out existing))
                return existing;

            var logger = _loggerFactory.CreateLogger<AtemUdpConnection>();
            var conn = new AtemUdpConnection(host, logger, port);

            // Ensure handshake / loops are running
            await conn.ConnectAsync(ct);

            _connections[deviceId] = conn;
            return conn;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var kvp in _connections)
        {
            try { await kvp.Value.DisposeAsync(); } catch { /* swallow on shutdown */ }
        }

        _connections.Clear();
        _lock.Dispose();
    }
}