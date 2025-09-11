# ProdControlAV.Agent - Raspberry Pi Device Monitoring Agent

A .NET 8 Worker Service designed for Raspberry Pi 5 running Pi OS 64-bit lite that provides comprehensive device monitoring and control capabilities for production audio/visual environments.

## Overview

The ProdControlAV Agent is a lightweight, secure monitoring service that:

- **Dynamically discovers devices** from the API server (`GET {BaseUrl}{DevicesEndpoint}`)
- **Probes device health** via ICMP ping or TCP SYN checks with configurable fallback
- **Reports state changes immediately** to minimize latency and API load
- **Sends periodic heartbeats** with full device snapshots for health monitoring
- **Executes secure remote commands** with whitelisted operations only
- **Provides comprehensive logging** and audit trails for all operations
- **Runs securely** as an unprivileged user with minimal required capabilities

## Architecture

```
┌─────────────────┐    HTTPS/API Key    ┌─────────────────┐
│                 │◄────────────────────►│                 │
│  ProdControlAV  │                     │  ProdControlAV  │
│     Agent       │                     │      API        │
│  (Raspberry Pi) │                     │    (Server)     │
└─────────────────┘                     └─────────────────┘
         │
         │ ICMP/TCP
         ▼
┌─────────────────┐
│   A/V Devices   │
│  (Cameras,      │
│   Switches,     │
│   Mixers, etc.) │
└─────────────────┘
```

## Features

### Device Monitoring
- **Concurrent probing** with configurable concurrency limits
- **ICMP ping** for general device health (default)
- **TCP SYN probes** for specific ports when ICMP is blocked
- **Configurable thresholds** for determining up/down states
- **Sub-second response times** with immediate state change reporting

### Security
- **API Key Authentication** with 32+ character minimum length requirement
- **Environment variable configuration** for secure credential management
- **Command whitelisting** - only predefined safe operations allowed
- **Privilege separation** - runs as non-root user with minimal capabilities
- **Encrypted communication** - HTTPS only for all API interactions

### Reliability
- **Automatic device discovery** with periodic refresh
- **Graceful error handling** with comprehensive logging
- **Network resilience** with configurable timeouts and retries
- **Systemd integration** for automatic startup and monitoring
- **Zero-downtime operation** during configuration changes

## Installation & Deployment

### Prerequisites

- Raspberry Pi 5 running Pi OS 64-bit lite
- .NET 8 Runtime (installed automatically with self-contained deployment)
- Network connectivity to both the API server and monitored devices
- CAP_NET_RAW capability for ICMP ping operations

### 1. Build for Raspberry Pi

On your development machine:

```bash
# Navigate to the Agent project directory
cd src/ProdControlAV.Agent

# Build for ARM64 (Raspberry Pi 5)
dotnet publish ./ProdControlAV.Agent.csproj \
    -c Release \
    -r linux-arm64 \
    --self-contained true \
    -o ./publish \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=true

# The publish folder will contain all necessary files
```

### 2. Transfer to Raspberry Pi

```bash
# Create application directory
sudo mkdir -p /opt/prodcontrolav/agent

# Copy published files (from development machine)
scp -r ./publish/* pi@your-pi-ip:/tmp/agent/

# On the Pi, move files to final location
sudo mv /tmp/agent/* /opt/prodcontrolav/agent/

# Create dedicated user for the service
sudo useradd -r -s /usr/sbin/nologin -d /opt/prodcontrolav prodctl 2>/dev/null || true

# Set ownership
sudo chown -R prodctl:prodctl /opt/prodcontrolav
```

### 3. Configure Security

```bash
# Install capability tools if not present
sudo apt-get update && sudo apt-get install -y libcap2-bin

# Grant ICMP capability (allows ping without root)
sudo setcap cap_net_raw+ep /opt/prodcontrolav/agent/ProdControlAV.Agent

# Verify capability was set
getcap /opt/prodcontrolav/agent/ProdControlAV.Agent
# Should output: /opt/prodcontrolav/agent/ProdControlAV.Agent = cap_net_raw+ep
```

### 4. Configure Environment Variables

```bash
# Create environment file for the service
sudo tee /opt/prodcontrolav/agent/.env << 'EOF'
# ProdControlAV Agent Configuration
PRODCONTROL_AGENT_APIKEY=your-secure-32plus-character-api-key-here
EOF

# Secure the environment file
sudo chown prodctl:prodctl /opt/prodcontrolav/agent/.env
sudo chmod 600 /opt/prodcontrolav/agent/.env
```

### 5. Install as Systemd Service

Create the service file:

```bash
sudo tee /etc/systemd/system/prodcontrolav-agent.service << 'EOF'
[Unit]
Description=ProdControlAV Device Monitoring Agent
After=network-online.target
Wants=network-online.target
StartLimitIntervalSec=0

[Service]
Type=notify
User=prodctl
Group=prodctl
WorkingDirectory=/opt/prodcontrolav/agent
ExecStart=/opt/prodcontrolav/agent/ProdControlAV.Agent
EnvironmentFile=/opt/prodcontrolav/agent/.env
Restart=always
RestartSec=5
KillSignal=SIGTERM
TimeoutStopSec=30

# Security settings
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ProtectHome=true
ReadWritePaths=/opt/prodcontrolav/agent
ProtectControlGroups=true
ProtectKernelModules=true
ProtectKernelTunables=true
RestrictRealtime=true
RestrictSUIDSGID=true
LockPersonality=true
MemoryDenyWriteExecute=true
RestrictNamespaces=true

# Network restrictions
IPAddressDeny=any
IPAddressAllow=localhost
IPAddressAllow=192.168.0.0/16
IPAddressAllow=10.0.0.0/8
IPAddressAllow=172.16.0.0/12

[Install]
WantedBy=multi-user.target
EOF
```

Enable and start the service:

```bash
# Reload systemd configuration
sudo systemctl daemon-reload

# Enable automatic startup
sudo systemctl enable prodcontrolav-agent

# Start the service
sudo systemctl start prodcontrolav-agent

# Check status
sudo systemctl status prodcontrolav-agent

# Follow logs
sudo journalctl -u prodcontrolav-agent -f
```

## Configuration

### Application Settings (`appsettings.json`)

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
    "BaseUrl": "https://your-api-server.com/api",
    "DevicesEndpoint": "/agents/devices",
    "StatusEndpoint": "/agents/status",
    "HeartbeatEndpoint": "/agents/heartbeat",
    "CommandsEndpoint": "/agents/commands/next",
    "CommandCompleteEndpoint": "/agents/commands/complete",
    "ApiKey": "",
    "RefreshDevicesSeconds": 30,
    "CommandPollIntervalSeconds": 10
  }
}
```

### Environment Variables

The agent supports secure configuration via environment variables with the `PRODCONTROL_` prefix:

- **`PRODCONTROL_AGENT_APIKEY`** - API key for authentication (required, 32+ characters)

### Configuration Options

#### Polling Settings
- **`IntervalMs`** - Milliseconds between device probe cycles (default: 5000)
- **`Concurrency`** - Maximum concurrent device probes (default: 64)
- **`PingTimeoutMs`** - Timeout for ping/TCP probes in milliseconds (default: 700)
- **`TcpFallbackPort`** - Default TCP port for devices that block ICMP (optional)
- **`FailuresToDown`** - Consecutive failures before marking device down (default: 2)
- **`SuccessesToUp`** - Consecutive successes before marking device up (default: 1)
- **`HeartbeatSeconds`** - Interval between heartbeat transmissions (default: 60)

#### API Settings
- **`BaseUrl`** - Base URL of the ProdControlAV API server
- **`ApiKey`** - Authentication key (use environment variable)
- **`RefreshDevicesSeconds`** - How often to refresh device list from API (default: 30)
- **`CommandPollIntervalSeconds`** - How often to check for new commands (default: 10)

## API Integration

### Device Discovery

The agent periodically fetches the device list from the API:

**Request:**
```http
GET {BaseUrl}/agents/devices?agentKey={ApiKey}
Accept: application/json
```

**Response:**
```json
[
  {
    "id": "camera-001",
    "ipAddress": "192.168.1.100",
    "type": "Camera",
    "tcpPort": null
  },
  {
    "id": "switch-001",
    "ipAddress": "192.168.1.200",
    "type": "NetworkSwitch",
    "tcpPort": 23
  }
]
```

### Status Reporting

When device states change, the agent immediately reports to the API:

**Request:**
```http
POST {BaseUrl}/agents/status
Content-Type: application/json

{
  "agentKey": "your-api-key",
  "tenantId": null,
  "readings": [
    {
      "deviceId": "camera-001",
      "isOnline": true,
      "latencyMs": null,
      "message": "Camera-001 (192.168.1.100) is ONLINE"
    }
  ]
}
```

### Heartbeat

Periodic heartbeats ensure the agent is alive and provides system information:

**Request:**
```http
POST {BaseUrl}/agents/heartbeat
Content-Type: application/json

{
  "agentKey": "your-api-key",
  "hostname": "prodcontrol-pi-001",
  "ipAddress": "192.168.1.50",
  "version": "1.0.0"
}
```

### Command Execution

The agent polls for and executes commands securely:

**Command Poll Request:**
```http
POST {BaseUrl}/agents/commands/next
Content-Type: application/json

{
  "agentKey": "your-api-key",
  "max": 10
}
```

**Command Poll Response:**
```json
{
  "commands": [
    {
      "commandId": "cmd-12345",
      "deviceId": "camera-001",
      "verb": "PING",
      "payload": null
    }
  ]
}
```

**Command Completion Report:**
```http
POST {BaseUrl}/agents/commands/complete
Content-Type: application/json

{
  "agentKey": "your-api-key",
  "commandId": "cmd-12345",
  "success": true,
  "message": "Ping successful - 5ms response time",
  "durationMs": 10
}
```

## Security Considerations

### API Key Management
- API keys must be at least 32 characters long
- Store API keys in environment variables, never in configuration files
- Use different API keys for each agent/environment
- Rotate API keys regularly

### Network Security
- Use HTTPS for all API communications
- Configure firewall rules to restrict agent network access
- Consider VPN or private network for sensitive environments
- Monitor network traffic for anomalies

### System Security
- Agent runs as unprivileged user with minimal capabilities
- Uses systemd security features to limit system access
- All operations are logged for audit purposes
- Command execution is strictly limited to whitelisted operations

## Monitoring & Troubleshooting

### Service Status
```bash
# Check service status
sudo systemctl status prodcontrolav-agent

# View recent logs
sudo journalctl -u prodcontrolav-agent --since "1 hour ago"

# Follow live logs
sudo journalctl -u prodcontrolav-agent -f

# Check for errors
sudo journalctl -u prodcontrolav-agent -p err
```

### Performance Monitoring
```bash
# Check resource usage
top -p $(pgrep -f ProdControlAV.Agent)

# Monitor network connections
sudo netstat -tulpn | grep ProdControlAV

# Check capability status
getcap /opt/prodcontrolav/agent/ProdControlAV.Agent
```

### Common Issues

**Agent won't start:**
- Check API key configuration
- Verify network connectivity to API server
- Check systemd service logs
- Ensure proper file permissions

**ICMP ping not working:**
- Verify CAP_NET_RAW capability is set
- Check if target devices respond to ping manually
- Consider using TCP fallback for problematic devices

**High CPU usage:**
- Reduce polling frequency (`IntervalMs`)
- Lower concurrency setting
- Check for network issues causing timeouts

**Memory usage growing:**
- Monitor for network connection leaks
- Check API response sizes
- Restart service if memory usage is excessive

### Updating the Agent

To update to a new version:

```bash
# Stop the service
sudo systemctl stop prodcontrolav-agent

# Backup current installation
sudo cp -r /opt/prodcontrolav/agent /opt/prodcontrolav/agent.backup

# Deploy new version (follow deployment steps)
# ...

# Start the service
sudo systemctl start prodcontrolav-agent

# Verify operation
sudo systemctl status prodcontrolav-agent
```

## Development

### Building from Source
```bash
# Restore dependencies
dotnet restore

# Build for development
dotnet build

# Run tests
dotnet test

# Publish for production
dotnet publish -c Release -r linux-arm64 --self-contained true
```

### Testing
The agent includes comprehensive unit tests covering:
- Device discovery and management
- Status reporting and heartbeat functionality
- Command execution and security
- Error handling and resilience
- API communication

Run tests with: `dotnet test`

## Support

For issues, feature requests, or contributions, please refer to the main ProdControlAV repository documentation.

## License

This project is part of the ProdControlAV system. Refer to the main repository for licensing information.
