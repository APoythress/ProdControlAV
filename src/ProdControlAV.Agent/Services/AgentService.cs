using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using ProdControlAV.Agent.Models;

namespace ProdControlAV.Agent.Services;

public sealed class AgentService : BackgroundService
{
    private readonly AgentOptions _opt;
    private readonly ApiOptions _apiOpt;
    private readonly IDeviceSource _deviceSource;
    private readonly IStatusPublisher _publisher;
    private readonly ICommandService _commandService;
    private readonly ILogger<AgentService> _logger;

    private SemaphoreSlim _gate = null!;
    private PeriodicTimer _tick = null!;
    private PeriodicTimer _heartbeat = null!;
    private PeriodicTimer _commandPoll = null!;

    private readonly ConcurrentDictionary<string, State> _state = new();

    private sealed class State
    {
        public bool IsUp;
        public int FailStreak;
        public int OkStreak;
        public DateTimeOffset ChangedAt = DateTimeOffset.UtcNow;
        public string Id = "";
        public string Name = "";
        public string Ip = "";
    }

    public AgentService(
        IOptions<AgentOptions> opt,
        IOptions<ApiOptions> apiOpt,
        IDeviceSource deviceSource,
        IStatusPublisher publisher,
        ICommandService commandService,
        ILogger<AgentService> logger)
    {
        _opt = opt.Value;
        _apiOpt = apiOpt.Value;
        _deviceSource = deviceSource;
        _publisher = publisher;
        _commandService = commandService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _gate = new SemaphoreSlim(_opt.Concurrency);
        _tick = new PeriodicTimer(TimeSpan.FromMilliseconds(_opt.IntervalMs));
        _heartbeat = new PeriodicTimer(TimeSpan.FromSeconds(_opt.HeartbeatSeconds));
        _commandPoll = new PeriodicTimer(TimeSpan.FromSeconds(_apiOpt.CommandPollIntervalSeconds));

        // Wait once for device source to populate initially
        await Task.Delay(1000, stoppingToken);

        _ = RunPollLoop(stoppingToken);
        _ = RunHeartbeatLoop(stoppingToken);
        _ = RunCommandPollLoop(stoppingToken);
    }

    private async Task RunPollLoop(CancellationToken ct)
    {
        var rnd = new Random();

        while (await _tick.WaitForNextTickAsync(ct))
        {
            var devices = _deviceSource.Current;
            EnsureState(devices);

            var tasks = new List<Task>(devices.Count);
            foreach (var d in devices)
            {
                await _gate.WaitAsync(ct);
                var delay = rnd.Next(0, 200);
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(delay, ct);
                        var up = d.PreferTcp && _opt.TcpFallbackPort is int p
                            ? await TcpProbeAsync(d.Ip, p, _opt.PingTimeoutMs, ct)
                            : await IcmpProbeAsync(d.Ip, _opt.PingTimeoutMs, ct);

                        await UpdateStateAndPublishIfChanged(d, up, ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogDebug(ex, "Probe error for {Ip}", d.Ip);
                        await UpdateStateAndPublishIfChanged(d, up: false, ct);
                    }
                    finally
                    {
                        _gate.Release();
                    }
                }, ct));
            }

            await Task.WhenAll(tasks);
        }
    }

    private void EnsureState(IEnumerable<Device> devices)
    {
        foreach (var d in devices)
        {
            _state.AddOrUpdate(d.Id, _ => new State { Id = d.Id, Name = d.Name, Ip = d.Ip },
                (_, s) => { s.Name = d.Name; s.Ip = d.Ip; return s; });
        }

        // remove state for devices no longer present
        var activeIds = devices.Select(d => d.Id).ToHashSet();
        foreach (var key in _state.Keys)
        {
            if (!activeIds.Contains(key))
                _state.TryRemove(key, out _);
        }
    }

    private async Task RunHeartbeatLoop(CancellationToken ct)
    {
        while (await _heartbeat.WaitForNextTickAsync(ct))
        {
            var snapshot = _state.Values
                .Select(s => new DeviceStatus(s.Id, s.Name, s.Ip, s.IsUp ? "ONLINE" : "OFFLINE", s.ChangedAt))
                .ToArray();

            try { await _publisher.HeartbeatAsync(snapshot, ct); }
            catch { /* ignore */ }
        }
    }

    private async Task UpdateStateAndPublishIfChanged(Device d, bool up, CancellationToken ct)
    {
        var s = _state.GetOrAdd(d.Id, _ => new State { Id = d.Id, Name = d.Name, Ip = d.Ip });

        if (up)
        {
            s.OkStreak++;
            s.FailStreak = 0;
            if (!s.IsUp && s.OkStreak >= _opt.SuccessesToUp)
            {
                s.IsUp = true;
                s.ChangedAt = DateTimeOffset.UtcNow;
                await _publisher.PublishAsync(new DeviceStatus(d.Id, d.Name, d.Ip, "ONLINE", s.ChangedAt), ct);
            }
        }
        else
        {
            s.FailStreak++;
            s.OkStreak = 0;
            if (s.IsUp && s.FailStreak >= _opt.FailuresToDown)
            {
                s.IsUp = false;
                s.ChangedAt = DateTimeOffset.UtcNow;
                await _publisher.PublishAsync(new DeviceStatus(d.Id, d.Name, d.Ip, "OFFLINE", s.ChangedAt), ct);
            }
        }
    }

    private async Task RunCommandPollLoop(CancellationToken ct)
    {
        while (await _commandPoll.WaitForNextTickAsync(ct))
        {
            try
            {
                var commands = await _commandService.PollCommandsAsync(ct);
                if (commands.Count > 0)
                {
                    _logger.LogInformation("Received {Count} commands to execute", commands.Count);
                    
                    // Execute commands concurrently but limit concurrency
                    var semaphore = new SemaphoreSlim(Math.Min(5, commands.Count));
                    var tasks = commands.Select(async cmd =>
                    {
                        await semaphore.WaitAsync(ct);
                        try
                        {
                            await _commandService.ExecuteCommandAsync(cmd, ct);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    });

                    await Task.WhenAll(tasks);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error in command polling loop");
            }
        }
    }

    private static async Task<bool> IcmpProbeAsync(string ip, int timeoutMs, CancellationToken ct)
    {
        using var ping = new Ping();
        var reply = await ping.SendPingAsync(ip, timeoutMs);
        return reply.Status == IPStatus.Success;
    }

    private static async Task<bool> TcpProbeAsync(string ip, int port, int timeoutMs, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(ip, port, cts.Token);
            return true;
        }
        catch { return false; }
    }
}
