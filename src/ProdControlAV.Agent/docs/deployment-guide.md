# Raspberry Pi Deployment Guide

Comprehensive guide for deploying ProdControlAV.Agent on Raspberry Pi devices.

## Hardware Requirements

### Recommended Hardware
- **Raspberry Pi 5 (4GB RAM)**: Optimal performance for monitoring multiple devices
- **Raspberry Pi 4 Model B (4GB RAM)**: Adequate for smaller deployments
- **32GB+ Class 10 MicroSD Card**: Fast storage for reliable operation
- **Power Supply**: Official Raspberry Pi USB-C power supply (5.1V/3A)
- **Network Connection**: Ethernet preferred for stability, Wi-Fi acceptable

### Minimum Requirements
- Raspberry Pi 4 Model B (2GB RAM)
- 16GB Class 10 MicroSD Card
- Stable network connection to API server and monitored devices
- Network capabilities for ICMP ping (requires CAP_NET_RAW)

## Operating System Setup

### 1. Flash Raspberry Pi OS
Use the official Raspberry Pi Imager:

1. Download [Raspberry Pi Imager](https://www.raspberrypi.com/software/)
2. Select **Raspberry Pi OS Lite (64-bit)** for headless operation
3. Configure advanced options:
   - Enable SSH with public key authentication
   - Set username and password
   - Configure Wi-Fi (if not using Ethernet)
   - Set locale settings

### 2. Initial System Configuration

#### Update System
```bash
sudo apt update && sudo apt upgrade -y
sudo reboot
```

#### Configure Network (if needed)
For static IP configuration, edit `/etc/dhcpcd.conf`:
```bash
sudo nano /etc/dhcpcd.conf

# Add static IP configuration
interface eth0
static ip_address=192.168.1.100/24
static routers=192.168.1.1
static domain_name_servers=8.8.8.8 8.8.4.4
```

#### Install .NET 8 Runtime
```bash
# Add Microsoft repository
wget https://packages.microsoft.com/config/debian/11/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb

# Install .NET 8 Runtime
sudo apt update
sudo apt install -y aspnetcore-runtime-8.0

# Verify installation
dotnet --version
```

## Manual Deployment

### 1. Build Application
On your development machine:

```bash
cd ProdControlAV
dotnet publish src/ProdControlAV.Agent/ProdControlAV.Agent.csproj \
    -c Release \
    -r linux-arm64 \
    --self-contained true \
    -o ./publish/agent
```

### 2. Transfer Files
Copy the published application to the Raspberry Pi:

```bash
# Create target directory on Pi
ssh pi@192.168.1.100 'sudo mkdir -p /opt/prodcontrolav/agent'

# Transfer application files
scp -r ./publish/agent/* pi@192.168.1.100:/tmp/agent/

# Move files to final location
ssh pi@192.168.1.100 'sudo mv /tmp/agent/* /opt/prodcontrolav/agent/'
```

### 3. Create Service User
Create a dedicated user for running the service:

```bash
# Connect to Raspberry Pi
ssh pi@192.168.1.100

# Create service user
sudo useradd --system --no-create-home --shell /usr/sbin/nologin prodctl

# Set ownership and permissions
sudo chown -R prodctl:prodctl /opt/prodcontrolav/agent
sudo chmod +x /opt/prodcontrolav/agent/ProdControlAV.Agent
```

### 4. Configure Environment
Create environment configuration file:

```bash
# Create environment file
sudo tee /opt/prodcontrolav/agent/.env << 'EOF'
# ProdControlAV Agent Configuration
PRODCONTROL_API_URL=https://your-api-server.com/api
PRODCONTROL_AGENT_APIKEY=your-secure-32plus-character-api-key-here
ASPNETCORE_ENVIRONMENT=Production
EOF

# Secure the environment file
sudo chown prodctl:prodctl /opt/prodcontrolav/agent/.env
sudo chmod 600 /opt/prodcontrolav/agent/.env
```

### 5. Create Systemd Service
Create the service definition:

```bash
sudo tee /etc/systemd/system/prodcontrolav-agent.service << 'EOF'
[Unit]
Description=ProdControlAV Monitoring Agent
After=network-online.target
Wants=network-online.target

[Service]
Type=notify
User=prodctl
Group=prodctl
WorkingDirectory=/opt/prodcontrolav/agent
ExecStart=/opt/prodcontrolav/agent/ProdControlAV.Agent
Restart=always
RestartSec=10
KillSignal=SIGTERM
SyslogIdentifier=prodcontrolav-agent
EnvironmentFile=/opt/prodcontrolav/agent/.env

# Security settings
NoNewPrivileges=yes
PrivateTmp=yes
ProtectSystem=strict
ProtectHome=yes
ReadWritePaths=/opt/prodcontrolav/agent/data
ReadWritePaths=/var/log

# Network capabilities for ICMP ping
AmbientCapabilities=CAP_NET_RAW
CapabilityBoundingSet=CAP_NET_RAW

[Install]
WantedBy=multi-user.target
EOF
```

### 6. Enable and Start Service
```bash
# Reload systemd configuration
sudo systemctl daemon-reload

# Enable service for automatic startup
sudo systemctl enable prodcontrolav-agent

# Start the service
sudo systemctl start prodcontrolav-agent

# Check service status
sudo systemctl status prodcontrolav-agent
```

## Automated Deployment with Scripts

### Using deploy-agent.sh
The repository includes automated deployment scripts:

```bash
# From your development machine
cd ProdControlAV

# Deploy to Raspberry Pi
./scripts/deploy-agent.sh \
    --pi-host 192.168.1.100 \
    --pi-user pi \
    --api-url https://your-server.com/api \
    --api-key "your-secure-api-key-here"
```

### Interactive Deployment
For guided deployment:

```bash
./scripts/quick-deploy.sh
```

This script will prompt you for all required information and handle the deployment process.

## Configuration Management

### Environment Variables
The agent supports configuration via environment variables:

| Variable | Description | Example |
|----------|-------------|---------|
| `PRODCONTROL_API_URL` | API server base URL | `https://api.example.com` |
| `PRODCONTROL_AGENT_APIKEY` | Agent authentication key | 32+ character string |
| `PRODCONTROL_AGENT_NAME` | Agent display name | `Studio-A-Monitor` |
| `PRODCONTROL_AGENT_LOCATION` | Physical location | `Studio A Equipment Rack` |
| `ASPNETCORE_ENVIRONMENT` | Application environment | `Production` |

### Configuration Files
Settings can also be configured via `appsettings.json`:

```json
{
    "Agent": {
        "ApiBase": "https://your-server.com/api",
        "TenantId": "00000000-0000-0000-0000-000000000000",
        "AgentKey": "your-api-key",
        "IntervalSeconds": 15,
        "Name": "Pi-Studio-A",
        "Location": "Studio A Equipment Rack"
    },
    "Logging": {
        "LogLevel": {
            "Default": "Information",
            "Microsoft": "Warning",
            "Microsoft.Hosting.Lifetime": "Information"
        }
    }
}
```

## Network Configuration

### Firewall Rules
Configure firewall to allow necessary traffic:

```bash
# Install and configure UFW
sudo apt install ufw

# Allow SSH
sudo ufw allow ssh

# Allow outbound HTTPS to API server
sudo ufw allow out 443

# Allow outbound HTTP for device communication
sudo ufw allow out 80

# Allow ICMP for device monitoring
sudo ufw allow out to any port 7 proto icmp

# Enable firewall
sudo ufw --force enable
```

### Network Capabilities
The agent requires network capabilities for ICMP ping:

```bash
# Verify capabilities are set
sudo getcap /opt/prodcontrolav/agent/ProdControlAV.Agent

# If capabilities are missing, set them manually
sudo setcap cap_net_raw+ep /opt/prodcontrolav/agent/ProdControlAV.Agent
```

## Monitoring and Troubleshooting

### Service Logs
View agent logs using journalctl:

```bash
# View recent logs
sudo journalctl -u prodcontrolav-agent -f

# View logs since last boot
sudo journalctl -u prodcontrolav-agent -b

# View logs for specific time range
sudo journalctl -u prodcontrolav-agent --since "2024-01-01" --until "2024-01-02"
```

### Service Management
Common service management commands:

```bash
# Check service status
sudo systemctl status prodcontrolav-agent

# Start/stop service
sudo systemctl start prodcontrolav-agent
sudo systemctl stop prodcontrolav-agent

# Restart service
sudo systemctl restart prodcontrolav-agent

# View service logs
sudo journalctl -u prodcontrolav-agent

# Check if service is enabled
sudo systemctl is-enabled prodcontrolav-agent
```

### Health Checks
Verify agent functionality:

```bash
# Check if agent is running
ps aux | grep ProdControlAV.Agent

# Test network connectivity to API
curl -k https://your-server.com/api/health

# Check agent API communication
sudo journalctl -u prodcontrolav-agent | grep "API"

# Verify device monitoring
sudo journalctl -u prodcontrolav-agent | grep "ping"
```

### Common Issues

#### Service Won't Start
```bash
# Check service status for errors
sudo systemctl status prodcontrolav-agent

# Check dependencies
sudo systemctl list-dependencies prodcontrolav-agent

# Verify file permissions
ls -la /opt/prodcontrolav/agent/
```

#### Network Connectivity Issues
```bash
# Test API connectivity
curl -v https://your-server.com/api/health

# Check DNS resolution
nslookup your-server.com

# Test device ping manually
ping -c 4 192.168.1.100
```

#### Permission Issues
```bash
# Check user and group ownership
ls -la /opt/prodcontrolav/agent/

# Verify service user exists
id prodctl

# Check systemd service file permissions
ls -la /etc/systemd/system/prodcontrolav-agent.service
```

## Performance Tuning

### Memory Optimization
For devices with limited memory:

```bash
# Add swap file (if not present)
sudo dd if=/dev/zero of=/swapfile bs=1024 count=1024000
sudo chmod 600 /swapfile
sudo mkswap /swapfile
sudo swapon /swapfile

# Make swap permanent
echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
```

### CPU Optimization
Adjust monitoring intervals for CPU-constrained devices:

```json
{
    "Agent": {
        "IntervalSeconds": 30,  // Increase from default 15 seconds
        "ConcurrentDevices": 10  // Reduce concurrent monitoring
    }
}
```

## Security Hardening

### System Security
Apply security best practices:

```bash
# Update system packages regularly
sudo apt update && sudo apt upgrade

# Configure automatic security updates
sudo apt install unattended-upgrades
sudo dpkg-reconfigure unattended-upgrades

# Secure SSH configuration
sudo nano /etc/ssh/sshd_config
# Set: PasswordAuthentication no
# Set: PermitRootLogin no
# Set: AllowUsers pi
sudo systemctl restart sshd
```

### Application Security
- Use strong API keys (32+ characters)
- Rotate API keys regularly
- Monitor agent logs for suspicious activity
- Restrict network access using firewall rules

## Backup and Recovery

### Configuration Backup
```bash
# Backup configuration files
sudo tar -czf prodcontrolav-backup-$(date +%Y%m%d).tar.gz \
    /opt/prodcontrolav/agent/.env \
    /etc/systemd/system/prodcontrolav-agent.service \
    /opt/prodcontrolav/agent/appsettings.json
```

### Recovery Procedure
```bash
# Stop service
sudo systemctl stop prodcontrolav-agent

# Restore configuration
sudo tar -xzf prodcontrolav-backup-20240101.tar.gz -C /

# Reload systemd and restart
sudo systemctl daemon-reload
sudo systemctl start prodcontrolav-agent
```

## Scaling Considerations

### Multiple Agents
For large deployments:
- Deploy one agent per network segment or location
- Use descriptive agent names and locations
- Monitor agent health through the API dashboard
- Load balance device monitoring across agents

### High Availability
For critical environments:
- Deploy redundant agents for failover
- Use network monitoring to detect agent failures
- Implement automated agent replacement procedures
- Monitor API connectivity and failover capabilities

This completes the comprehensive deployment guide for ProdControlAV.Agent on Raspberry Pi devices.