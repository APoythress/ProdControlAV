# ProdControlAV Agent Deployment Scripts

This directory contains deployment automation scripts for the ProdControlAV Agent.

## Quick Start

### Option 1: Interactive Guided Deployment
Run the interactive script and follow the prompts:

```bash
./scripts/quick-deploy.sh
```

### Option 2: Direct Command-Line Deployment
Deploy the ProdControlAV Agent to your Raspberry Pi in one command:

```bash
./scripts/deploy-agent.sh \
  --pi-host 192.168.1.100 \
  --pi-user pi \
  --api-url https://your-server.com/api \
  --api-key "your-secure-32-character-api-key-here"
```

## Scripts Overview

### `deploy-agent.sh`

**Main deployment orchestration script** - Handles the complete end-to-end deployment process from your development environment to the Raspberry Pi.

### `quick-deploy.sh`

**Interactive deployment helper** - Provides a user-friendly guided interface that prompts for all required information and calls the main deployment script.

**What it does:**
1. ✅ Validates all parameters and prerequisites
2. 🧪 Runs tests to ensure code quality
3. 🏗️ Builds the agent for ARM64 (using Docker or dotnet publish)
4. 📤 Transfers files to Raspberry Pi via SCP
5. 🚀 Deploys and configures the agent on the Pi
6. ⚙️ Installs and starts the systemd service
7. 📊 Provides comprehensive status logging

**Key Features:**
- **Secure credential handling** - Credentials passed as parameters, never stored in files
- **Comprehensive validation** - Validates SSH connectivity, API endpoints, and security requirements
- **Flexible build options** - Supports Docker or native dotnet publish
- **Automatic rollback** - Creates backups of existing installations
- **Detailed logging** - Progress updates throughout the deployment process

## Usage

### Required Parameters

| Parameter | Description | Example |
|-----------|-------------|---------|
| `--pi-host` | Raspberry Pi hostname or IP address | `192.168.1.100` or `raspberrypi.local` |
| `--pi-user` | SSH username for the Raspberry Pi | `pi` |
| `--api-url` | API server base URL | `https://your-server.com/api` |
| `--api-key` | Agent API key (32+ characters) | `your-secure-api-key-min-32-chars` |

### Optional Parameters

| Parameter | Description | Default |
|-----------|-------------|---------|
| `--pi-ssh-port` | SSH port for Raspberry Pi | `22` |
| `--skip-build` | Skip building (use existing build) | `false` |
| `--skip-tests` | Skip running tests | `false` |
| `--build-config` | Build configuration | `Release` |
| `--no-docker` | Use dotnet publish instead of Docker | `false` |
| `--verbose` | Enable verbose logging | `false` |
| `--help` | Show help message | - |

### Examples

#### Basic Deployment
```bash
./scripts/deploy-agent.sh \
  --pi-host 192.168.1.100 \
  --pi-user pi \
  --api-url https://my-server.com/api \
  --api-key "abcdef1234567890abcdef1234567890"
```

#### Advanced Deployment with Custom Options
```bash
./scripts/deploy-agent.sh \
  --pi-host raspberrypi.local \
  --pi-user admin \
  --pi-ssh-port 2222 \
  --api-url https://my-server.com/api \
  --api-key "your-secure-api-key-here" \
  --build-config Debug \
  --verbose
```

#### Quick Redeploy (Skip Build and Tests)
```bash
./scripts/deploy-agent.sh \
  --pi-host 192.168.1.100 \
  --pi-user pi \
  --api-url https://my-server.com/api \
  --api-key "your-secure-api-key-here" \
  --skip-build \
  --skip-tests
```

## Prerequisites

### Development Machine
- .NET 8 SDK
- Docker (recommended) or use `--no-docker` for native builds
- SSH client (`ssh` and `scp` commands)
- Network connectivity to Raspberry Pi

### Raspberry Pi
- Raspberry Pi 5 with Pi OS 64-bit
- SSH enabled with key-based authentication configured
- Network connectivity to API server and monitored devices
- Sudo access for the SSH user

## Security Considerations

### SSH Authentication
The script uses SSH key-based authentication. Ensure your SSH public key is added to the Pi's `~/.ssh/authorized_keys` file:

```bash
# On your development machine
ssh-copy-id pi@192.168.1.100
```

### API Key Security
- API keys must be at least 32 characters long
- Keys are passed as command-line parameters (not stored in source control)
- Keys are securely stored in `/opt/prodcontrolav/agent/.env` on the Pi with restricted permissions (600)

### Network Security
- HTTPS is recommended for API communication
- The systemd service includes network restrictions for the private IP ranges
- ICMP capabilities are granted specifically for ping functionality

## Troubleshooting

### Common Issues

#### SSH Connection Failed
```
[ERROR] Cannot connect to pi@192.168.1.100:22
```
**Solutions:**
- Verify the Pi is powered on and connected to the network
- Check if SSH is enabled: `sudo systemctl enable ssh && sudo systemctl start ssh`
- Verify SSH key authentication: `ssh pi@192.168.1.100`
- Check hostname/IP address and port

#### Docker Build Failed
```
[ERROR] Docker build failed
```
**Solutions:**
- Ensure Docker is running: `docker info`
- Try using native build: `--no-docker`
- Check Docker daemon logs

#### API Key Validation Failed
```
[ERROR] API key must be at least 32 characters long
```
**Solutions:**
- Generate a secure API key: `openssl rand -base64 32`
- Ensure the key matches the server configuration

#### Service Failed to Start
```
[PI-WARNING] Agent service may have failed to start properly
```
**Solutions:**
- Check service status: `sudo systemctl status prodcontrolav-agent`
- View logs: `sudo journalctl -u prodcontrolav-agent -f`
- Verify API connectivity from the Pi
- Check environment file: `/opt/prodcontrolav/agent/.env`

### Debugging Commands

Run these commands on the Raspberry Pi to troubleshoot issues:

```bash
# Check service status
sudo systemctl status prodcontrolav-agent

# View service logs
sudo journalctl -u prodcontrolav-agent -f

# Test API connectivity
curl -H "X-Api-Key: your-api-key" https://your-server.com/api/edge/heartbeat

# Check environment configuration
sudo cat /opt/prodcontrolav/agent/.env

# Test binary execution
sudo -u prodctl /opt/prodcontrolav/agent/ProdControlAV.Agent --help
```

## Service Management

Once deployed, manage the agent service on the Raspberry Pi:

```bash
# Start the service
sudo systemctl start prodcontrolav-agent

# Stop the service
sudo systemctl stop prodcontrolav-agent

# Restart the service
sudo systemctl restart prodcontrolav-agent

# Check service status
sudo systemctl status prodcontrolav-agent

# View live logs
sudo journalctl -u prodcontrolav-agent -f

# Enable service on boot
sudo systemctl enable prodcontrolav-agent

# Disable service on boot
sudo systemctl disable prodcontrolav-agent
```

## File Locations on Raspberry Pi

| File/Directory | Purpose |
|----------------|---------|
| `/opt/prodcontrolav/agent/` | Agent installation directory |
| `/opt/prodcontrolav/agent/ProdControlAV.Agent` | Main executable |
| `/opt/prodcontrolav/agent/.env` | Environment configuration (secure) |
| `/opt/prodcontrolav/agent/scripts/` | Deployment scripts |
| `/etc/systemd/system/prodcontrolav-agent.service` | Systemd service definition |

## Integration with CI/CD

The deployment script can be integrated into CI/CD pipelines:

```yaml
# Example GitHub Actions step
- name: Deploy to Raspberry Pi
  env:
    PI_HOST: ${{ secrets.PI_HOST }}
    PI_USER: ${{ secrets.PI_USER }}
    API_URL: ${{ secrets.API_URL }}
    API_KEY: ${{ secrets.API_KEY }}
  run: |
    ./scripts/deploy-agent.sh \
      --pi-host "$PI_HOST" \
      --pi-user "$PI_USER" \
      --api-url "$API_URL" \
      --api-key "$API_KEY"
```

For more information about the ProdControlAV Agent, see the [Agent README](../src/ProdControlAV.Agent/README.md).