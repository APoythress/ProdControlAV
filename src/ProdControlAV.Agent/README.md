# ProdControlAV.Agent (Dynamic Device List)

A .NET 8 Worker Service for Raspberry Pi 5 that:
- Pulls the **device list dynamically** from your API (`GET {BaseUrl}{DevicesEndpoint}`).
- Probes devices via **ICMP ping** (or TCP SYN if `PreferTcp` and `TcpFallbackPort`).
- Pushes **state changes immediately** to your API (`POST {BaseUrl}{StatusEndpoint}`).
- Sends optional **heartbeats** with full snapshot.
- **Polls for commands** from the API and executes them securely.
- **Reports command execution status** back to the API.
- Runs as a **systemd** service without root (uses CAP_NET_RAW for ICMP).

## Configuration (`appsettings.json`)
```json
{
  "Polling": {
    "IntervalMs": 5000,
    "Concurrency": 64,
    "PingTimeoutMs": 700,
    "TcpFallbackPort": null,
    "FailuresToDown": 2,
    "SuccessesToUp": 1,
    "HeartbeatSeconds": 60
  },
  "Api": {
    "BaseUrl": "https://localhost:5001/api",
    "DevicesEndpoint": "/agents/devices",
    "StatusEndpoint": "/agents/status",
    "HeartbeatEndpoint": "/agents/heartbeat",
    "CommandsEndpoint": "/agents/commands/next",
    "CommandCompleteEndpoint": "/agents/commands/complete",
    "ApiKey": "REPLACE_ME",
    "RefreshDevicesSeconds": 30,
    "CommandPollIntervalSeconds": 10
  }
}
```

### Expected device list schema from the API
`GET {BaseUrl}{DevicesEndpoint}?agentKey={ApiKey}` returns JSON:
```json
[
  { "id": "dev-1", "ipAddress": "192.168.1.100", "type": "ATEM", "tcpPort": null },
  { "id": "dev-2", "ipAddress": "192.168.1.247", "type": "WING", "tcpPort": 2222 }
]
```

### State change payload posted by the agent
`POST {BaseUrl}{StatusEndpoint}`
```json
{
  "agentKey": "your-api-key",
  "tenantId": null,
  "readings": [
    {
      "deviceId": "dev-1",
      "isOnline": true,
      "latencyMs": null,
      "message": "192.168.1.100 (192.168.1.100) is ONLINE"
    }
  ]
}
```

### Heartbeat payload posted by the agent
`POST {BaseUrl}{HeartbeatEndpoint}`
```json
{
  "agentKey": "your-api-key",
  "hostname": "raspberrypi",
  "ipAddress": null,
  "version": "1.0.0"
}
```

### Command Execution
The agent polls for commands from `POST {BaseUrl}{CommandsEndpoint}` and executes only whitelisted commands:
- `PING` - Controlled ping operation
- `STATUS` - Controlled status check

For security, only these predefined commands are executed. All command execution results are reported back via `POST {BaseUrl}{CommandCompleteEndpoint}`.

## Build & Publish (Pi 5)
```bash
dotnet publish ./ProdControlAV.Agent.csproj -c Release -r linux-arm64 --self-contained true -o ./publish
```

Copy to the Pi:
```bash
sudo mkdir -p /opt/prodcontrolav/agent
sudo cp -r ./publish/* /opt/prodcontrolav/agent/
sudo useradd -r -s /usr/sbin/nologin prodctl || true
sudo chown -R prodctl:prodctl /opt/prodcontrolav/agent
```

Grant ICMP capability to the binary (no root required at runtime):
```bash
sudo apt-get update && sudo apt-get install -y libcap2-bin
sudo setcap cap_net_raw+ep /opt/prodcontrolav/agent/ProdControlAV.Agent
```

Install systemd unit:
```bash
sudo cp /opt/prodcontrolav/agent/scripts/prodcontrolav-agent.service /etc/systemd/system/prodcontrolav-agent.service
sudo systemctl daemon-reload
sudo systemctl enable --now prodcontrolav-agent
journalctl -u prodcontrolav-agent -f
```

## Security Features
- **API Key Authentication**: All communications with the API use API key authentication
- **Command Whitelisting**: Only predefined, safe commands can be executed
- **Controlled Execution**: Commands are executed in a controlled manner with proper logging
- **No Root Required**: Runs as unprivileged user with only necessary network capabilities

## Notes
- If some devices block ICMP, set `tcpPort` for them in your DB and the agent will use TCP probes.
- Dashboard can poll your API every ~30s while the agent pushes on change → near-real-time UX without websockets (for now).
- Agent polls for commands every 10 seconds (configurable via `CommandPollIntervalSeconds`).
- All command executions are logged and status is reported back to the API for audit trail.
