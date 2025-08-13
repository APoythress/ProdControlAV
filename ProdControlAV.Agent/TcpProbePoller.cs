public sealed class TcpProbePoller : ILocalDevicePoller
{
    public async Task<IReadOnlyList<StatusReading>> CollectAsync(IReadOnlyList<DeviceTarget> devices, CancellationToken ct)
    {
        var list = new List<StatusReading>();
        foreach (var d in devices)
        {
            var port = d.TcpPort ?? 80;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var client = new System.Net.Sockets.TcpClient();
                var connectTask = client.ConnectAsync(d.IpAddress, port);
                var timeout = Task.Delay(TimeSpan.FromSeconds(2), ct);
                var completed = await Task.WhenAny(connectTask, timeout);
                if (completed == timeout) throw new TimeoutException("TCP connect timeout");

                sw.Stop();
                list.Add(new StatusReading
                {
                    DeviceId = d.Id,
                    IsOnline = client.Connected,
                    LatencyMs = (int)sw.ElapsedMilliseconds,
                    Message = client.Connected ? "tcp-ok" : "tcp-failed"
                });
            }
            catch (Exception ex)
            {
                sw.Stop();
                list.Add(new StatusReading
                {
                    DeviceId = d.Id,
                    IsOnline = false,
                    LatencyMs = (int)sw.ElapsedMilliseconds,
                    Message = $"tcp-error: {ex.GetType().Name}"
                });
            }
        }
        return list;
    }

    // Placeholder for command execution (telnet/http/etc)
    public Task<(bool Success, string Message)> ExecuteAsync(CommandEnvelope command, CancellationToken ct)
    {
        // route based on command.Verb, e.g., "wing.send", "atem.switch"
        return Task.FromResult((true, "noop-ok"));
    }
}