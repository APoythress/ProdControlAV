using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ProdControlAV.Agent.Interfaces;
using System.Net.Http;

namespace ProdControlAV.Agent.Services;

public sealed class AtemUdpConnectionManager : IAsyncDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConcurrentDictionary<Guid, AtemUdpConnection> _connections = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly IAtemStatePublisherFactory _publisherFactory;

    public AtemUdpConnectionManager(ILoggerFactory loggerFactory, IHttpClientFactory httpClientFactory, IAtemStatePublisherFactory publisherFactory)
    {
        _loggerFactory = loggerFactory;
        _httpClientFactory = httpClientFactory;
        _publisherFactory = publisherFactory;
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
            var httpClient = _httpClientFactory.CreateClient("AgentApi");
            var publisherLogger = _loggerFactory.CreateLogger<AtemStatePublisher>();
            var publisher = _publisherFactory.Create(httpClient, publisherLogger, deviceId);

            // Ensure handshake / loops are running
            await conn.ConnectAsync(ct);

            conn.StateChanged += async (_, state) =>
            {
                try
                {
                    await publisher.PublishAsync(state, ct);
                }
                catch (Exception ex)
                {
                    publisherLogger.LogWarning(ex, "Failed to publish ATEM state for device {DeviceId}", deviceId);
                }
            };

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
