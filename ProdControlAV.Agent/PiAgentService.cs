using Microsoft.Extensions.Options;
using System.Net.Http.Json;

public sealed class PiAgentService : BackgroundService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly AgentConfig _cfg;
    private readonly ILogger<PiAgentService> _log;
    private readonly ILocalDevicePoller _poller;

    public PiAgentService(IHttpClientFactory httpFactory, IOptions<AgentConfig> cfg, ILogger<PiAgentService> log, ILocalDevicePoller poller)
    {
        _httpFactory = httpFactory;
        _cfg = cfg.Value;
        _log = log;
        _poller = poller;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var http = _httpFactory.CreateClient("AgentApi");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // 1) Heartbeat
                await http.PostAsJsonAsync("/api/agents/heartbeat", new AgentHeartbeatRequest
                {
                    AgentKey = _cfg.AgentKey,
                    Hostname = Environment.MachineName,
                    IpAddress = GetLocalIp(),
                    Version = "1.0.0"
                }, ct);

                // 2) Fetch assigned devices
                var devices = await http.GetFromJsonAsync<List<DeviceTarget>>(
                    $"/api/agents/devices?agentKey={Uri.EscapeDataString(_cfg.AgentKey)}", ct
                ) ?? new List<DeviceTarget>();

                // 3) Probe locally
                var readings = await _poller.CollectAsync(devices, ct);

                // 4) Upload status
                await http.PostAsJsonAsync("/api/agents/status", new StatusUploadRequest
                {
                    AgentKey = _cfg.AgentKey,
                    TenantId = _cfg.TenantId,
                    Readings = readings.ToList()
                }, ct);

                // 5) Pull commands
                var nextResp = await http.PostAsJsonAsync("/api/agents/commands/next",
                    new CommandPullRequest { AgentKey = _cfg.AgentKey, Max = 10 }, ct);
                var next = await nextResp.Content.ReadFromJsonAsync<CommandPullResponse>(cancellationToken: ct)
                           ?? new CommandPullResponse();

                // 6) Execute & complete
                foreach (var cmd in next.Commands)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var (ok, msg) = await _poller.ExecuteAsync(cmd, ct);
                    sw.Stop();

                    await http.PostAsJsonAsync("/api/agents/commands/complete", new CommandCompleteRequest
                    {
                        AgentKey = _cfg.AgentKey,
                        CommandId = cmd.CommandId,
                        Success = ok,
                        Message = msg,
                        DurationMs = (int)sw.ElapsedMilliseconds
                    }, ct);
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Agent loop error");
            }

            await Task.Delay(TimeSpan.FromSeconds(_cfg.IntervalSeconds), ct);
        }
    }

    private static string? GetLocalIp()
    {
        try
        {
            var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
            var ip = host.AddressList.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            return ip?.ToString();
        }
        catch { return null; }
    }
}
