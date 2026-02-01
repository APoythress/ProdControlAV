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
- **Automatic updates** via NetSparkle with Ed25519 signature verification

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
PRODCONTROL_API_URL=https://your-api-server.com/api
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
# /etc/systemd/system/prodcontrolav-agent.service
[Unit]
Description=ProdControlAV Device Monitoring Agent
After=network-online.target
Wants=network-online.target
StartLimitIntervalSec=0

[Service]
Type=simple
User=prodctl
Group=prodctl
WorkingDirectory=/opt/prodcontrolav/agent
ExecStart=/opt/prodcontrolav/agent/ProdControlAV.Agent
EnvironmentFile=/opt/prodcontrolav/agent/.env

# Let systemd supply CAP_NET_RAW; do NOT rely on file caps
AmbientCapabilities=CAP_NET_RAW
CapabilityBoundingSet=CAP_NET_RAW

# Reasonable hardening that won't break .NET
NoNewPrivileges=false
PrivateTmp=true
ProtectSystem=full
ProtectHome=true
ReadWritePaths=/opt/prodcontrolav/agent
ProtectControlGroups=true
ProtectKernelModules=true
ProtectKernelTunables=true
RestrictRealtime=true
RestrictSUIDSGID=true
LockPersonality=true
# MemoryDenyWriteExecute DISABLED (JIT needs exec pages)
# MemoryDenyWriteExecute=true
RestrictNamespaces=true

# Network policy: allow local RFC1918 + loopback
IPAddressDeny=any
IPAddressAllow=localhost
IPAddressAllow=192.168.0.0/16
IPAddressAllow=10.0.0.0/8
IPAddressAllow=172.16.0.0/12

# Headroom for connections/files
LimitNOFILE=65536
TasksMax=infinity

Restart=always
RestartSec=3

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
  },
  "Update": {
    "Enabled": true,
    "AppcastUrl": "https://yourstorageaccount.blob.core.windows.net/updates/appcast.json",
    "Ed25519PublicKey": "",
    "CheckIntervalSeconds": 3600,
    "AutoInstall": true,
    "AppcastTimeoutSeconds": 30
  }
}
```

### Environment Variables

The agent supports secure configuration via environment variables with the `PRODCONTROL_` prefix:

- **`PRODCONTROL_API_URL`** - Base URL of the ProdControlAV API server (required if not in config)
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

#### Update Settings
- **`Enabled`** - Enable/disable automatic updates (default: true)
- **`AppcastUrl`** - URL to appcast.json manifest in Azure Blob Storage
- **`Ed25519PublicKey`** - Base64-encoded public key for verifying update signatures
- **`CheckIntervalSeconds`** - How often to check for updates (default: 3600 = 1 hour)
- **`AutoInstall`** - Automatically download and install updates (default: true)
- **`AppcastTimeoutSeconds`** - Timeout for downloading appcast manifest (default: 30 seconds)
  - Increase this value if experiencing timeout errors on slow network connections
  - The default NetSparkle timeout of 100 seconds is too long; 30 seconds provides faster failure detection
  - For very slow connections, increase to 60-120 seconds

**For complete automatic update setup instructions, see [AUTOMATIC-UPDATES-SETUP.md](../../AUTOMATIC-UPDATES-SETUP.md)**

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

### Update Service Logging

The agent includes dedicated file logging for the UpdateService to track all update-related activity:

**Log Location:** `/opt/prodcontrolav/agent/logs/updateService/`  
**File Format:** `YYYY-MM-DD_UpdateServiceLog.txt` (daily rotation)

**Features:**
- Captures all UpdateService operations and NetSparkle library events
- Detailed error logging with exception stack traces
- Separate log files per day (UTC) for easy troubleshooting
- Includes timestamps, log levels, and categorized messages
- Automatically created on first agent startup

**View Update Logs:**
```bash
# View today's update log
cat /opt/prodcontrolav/agent/logs/updateService/$(date -u +%Y-%m-%d)_UpdateServiceLog.txt

# Follow live update activity
tail -f /opt/prodcontrolav/agent/logs/updateService/$(date -u +%Y-%m-%d)_UpdateServiceLog.txt

# Search for errors in update logs
grep -i "error\|warn" /opt/prodcontrolav/agent/logs/updateService/*.txt

# View last 50 lines of today's update log
tail -50 /opt/prodcontrolav/agent/logs/updateService/$(date -u +%Y-%m-%d)_UpdateServiceLog.txt
```

**Common Update Issues:**
- **Timeout errors**: Check network connectivity and firewall settings for accessing update server
- **Appcast download failures**: Verify the AppcastUrl in appsettings.json is correct and accessible
- **Signature verification errors**: Ensure Ed25519PublicKey matches the update server's signing key

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

## Integration with Other Projects

### System Architecture Role
The ProdControlAV.Agent serves as the **Edge Computing Layer** in the distributed system:
- **Local Device Access**: Direct network access to A/V equipment on premises
- **Real-Time Monitoring**: Continuous device health monitoring with sub-second response
- **Command Execution**: Secure execution of device control commands from the API
- **Status Reporting**: Reliable status updates to the central API server

### Communication with ProdControlAV.API
- **HTTPS REST API**: All communication with API server uses encrypted HTTPS
- **API Key Authentication**: Each agent authenticates using a unique 32+ character API key
- **Heartbeat Protocol**: Regular heartbeat messages maintain connection and report agent health
- **Command Distribution**: API queues commands for agent execution via polling mechanism

### Data Flow Integration
```
ProdControlAV.WebApp → ProdControlAV.API → ProdControlAV.Agent → A/V Devices
A/V Devices → ProdControlAV.Agent → ProdControlAV.API → Database → ProdControlAV.WebApp
```

#### Device Status Reporting
1. Agent monitors devices using `ProdControlAV.Infrastructure.PingNetworkMonitor`
2. Status changes are immediately reported to API via `POST /api/agents/{id}/device-status`
3. API persists status changes using `ProdControlAV.Core.Models.DeviceStatusLog`
4. WebApp receives real-time updates through dashboard auto-refresh

#### Command Execution Flow
1. User initiates command through `ProdControlAV.WebApp` interface
2. WebApp sends command to `ProdControlAV.API` via REST endpoint
3. API creates `AgentCommand` record and queues for appropriate agent
4. Agent polls `GET /api/commands/pending/{agentId}` and retrieves commands
5. Agent executes command using `ProdControlAV.Infrastructure.TelnetDeviceController`
6. Agent reports execution result back to API for audit logging

### Shared Components
- **ProdControlAV.Core.Models**: Agent uses Device, AgentCommand, and DeviceStatusLog entities
- **ProdControlAV.Infrastructure.Services**: Agent implements network monitoring and device control services
- **Configuration**: Agent configuration aligns with API tenant and authentication requirements

## Support

For issues, feature requests, or contributions, please refer to the main ProdControlAV repository documentation.

### Related Documentation
- [System Overview](../../README.md) - Complete system architecture and project relationships
- [ProdControlAV.API](../ProdControlAV.API/README.md) - Backend API endpoints and authentication
- [ProdControlAV.Core](../ProdControlAV.Core/README.md) - Shared domain models and interfaces
- [ProdControlAV.Infrastructure](../ProdControlAV.Infrastructure/README.md) - Service implementations used by Agent
- [Deployment Scripts](../../scripts/README.md) - Automated deployment tools for Raspberry Pi

## License

This project is part of the ProdControlAV system. Refer to the main repository for licensing information.
