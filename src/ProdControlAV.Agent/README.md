# ProdControlAV.Agent (Dynamic Device List)

A .NET 8 Worker Service for Raspberry Pi 5 that:
- Pulls the **device list dynamically** from your API (`GET {BaseUrl}{DevicesEndpoint}`).
- Probes devices via **ICMP ping** (or TCP SYN if `PreferTcp` and `TcpFallbackPort`).
- Pushes **state changes immediately** to your API (`POST {BaseUrl}{StatusEndpoint}`).
- Sends optional **heartbeats** with full snapshot.
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
    "DevicesEndpoint": "/devices",
    "StatusEndpoint": "/agent/status",
    "HeartbeatEndpoint": "/agent/status/heartbeat",
    "ApiKey": "REPLACE_ME",
    "RefreshDevicesSeconds": 30
  }
}
```

### Expected device list schema from the API
`GET {BaseUrl}{DevicesEndpoint}` returns JSON:
```json
[
  { "id": "dev-1", "name": "ATEM TV Studio", "ip": "192.168.1.100", "preferTcp": false },
  { "id": "dev-2", "name": "Behringer WING", "ip": "192.168.1.247", "preferTcp": false }
]
```

### State change payload posted by the agent
`POST {BaseUrl}{StatusEndpoint}`
```json
{ "id":"dev-1","name":"ATEM TV Studio","ip":"192.168.1.100","state":"ONLINE","changedAtUtc":"2025-08-13T17:34:22Z" }
```

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

## Notes
- If some devices block ICMP, set `PreferTcp: true` for them in your DB and expose a known open port (and set `Polling:TcpFallbackPort`).
- Dashboard can poll your API every ~30s while the agent pushes on change → near-real-time UX without websockets (for now).
