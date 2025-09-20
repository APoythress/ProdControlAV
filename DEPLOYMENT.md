# ProdControlAV Agent Deployment Solution

## Overview

This deployment solution provides a streamlined, secure, and automated way to deploy the ProdControlAV Agent from your development environment directly to a Raspberry Pi. The solution handles the complete end-to-end process with comprehensive logging and error handling.

## What Problem Does This Solve?

Previously, deploying the ProdControlAV Agent required multiple manual steps:
1. Manually building the agent for ARM64
2. Manually copying files to the Raspberry Pi
3. Manually running deployment scripts on the Pi
4. Manually configuring credentials and services

This solution provides **a single command** that handles all these steps automatically with proper security and error handling.

## Key Features

### 🚀 **One-Command Deployment**
```bash
./scripts/deploy-agent.sh --pi-host 192.168.1.100 --pi-user pi --api-url https://your-server.com/api --api-key "your-secure-api-key"
```

### 🔒 **Secure Credential Handling**
- Credentials passed as command-line parameters (never stored in source control)
- API keys validated for minimum 32-character security requirement
- Environment files created with restricted permissions (600) on the Pi
- SSH key-based authentication required

### 🏗️ **Flexible Build Options**
- Docker-based builds (default) for consistent ARM64 compilation
- Native dotnet publish fallback option (`--no-docker`)
- Support for Debug/Release configurations
- Automatic dependency resolution and trimming

### 📋 **Comprehensive Validation**
- SSH connectivity testing before deployment
- Parameter validation (URL format, key length, etc.)
- Prerequisites checking (required tools, repository location)
- Build verification and error reporting

### 📊 **Detailed Logging**
- Color-coded status messages throughout the process
- Step-by-step progress indicators
- Verbose mode for debugging (`--verbose`)
- Clear error messages with actionable guidance

### 🔄 **Automatic Service Management**
- Systemd service installation and configuration
- Automatic service enablement for boot startup
- Service status verification after deployment
- Backup of existing installations before updates

## Usage Examples

### Basic Deployment
```bash
./scripts/deploy-agent.sh \
  --pi-host 192.168.1.100 \
  --pi-user pi \
  --api-url https://my-server.com/api \
  --api-key "abcdef1234567890abcdef1234567890"
```

### Advanced Deployment with Custom Options
```bash
./scripts/deploy-agent.sh \
  --pi-host raspberrypi.local \
  --pi-user admin \
  --pi-ssh-port 2222 \
  --api-url https://my-server.com/api \
  --api-key "your-secure-api-key-here" \
  --build-config Debug \
  --no-docker \
  --verbose
```

### Interactive Guided Deployment
```bash
./scripts/quick-deploy.sh
```
This script provides a user-friendly interface that prompts for all required information.

### Quick Redeploy (Skip Build and Tests)
```bash
./scripts/deploy-agent.sh \
  --pi-host 192.168.1.100 \
  --pi-user pi \
  --api-url https://my-server.com/api \
  --api-key "your-secure-api-key-here" \
  --skip-build \
  --skip-tests
```

## Deployment Process Flow

1. **🔍 Validation Phase**
   - Validate all input parameters
   - Check prerequisites (dotnet, docker, ssh, scp)
   - Test SSH connectivity to Raspberry Pi

2. **🧪 Testing Phase** (unless `--skip-tests`)
   - Run the full test suite
   - Abort deployment if any tests fail

3. **🏗️ Build Phase** (unless `--skip-build`)
   - Build agent for ARM64 using Docker or dotnet publish
   - Verify binary creation and architecture
   - Package all required files

4. **📤 Transfer Phase**
   - Create temporary directory on Raspberry Pi
   - Transfer all built files via SCP
   - Verify successful transfer

5. **🚀 Deployment Phase**
   - Execute remote deployment script on Pi
   - Stop existing service if running
   - Backup current installation
   - Install new files and set permissions
   - Create prodctl user and set capabilities
   - Configure environment variables

6. **⚙️ Service Configuration Phase**
   - Install systemd service definition
   - Enable service for automatic startup
   - Start the service
   - Verify service is running

7. **✅ Verification Phase**
   - Check service status
   - Provide management commands
   - Clean up temporary files

## Security Considerations

### SSH Authentication
- Requires SSH key-based authentication
- No password-based authentication supported
- Connection tested before proceeding with deployment

### API Key Security
- Minimum 32-character requirement enforced
- Keys never stored in source control or logs
- Securely written to environment file with restricted permissions

### Network Security
- HTTPS recommended for API communication
- Systemd service includes network restrictions
- ICMP capabilities granted specifically for ping functionality

### User Security
- Dedicated `prodctl` user created for the service
- Service runs with minimal privileges
- No shell access for the service user

## File Locations After Deployment

| Location | Purpose |
|----------|---------|
| `/opt/prodcontrolav/agent/` | Main application directory |
| `/opt/prodcontrolav/agent/ProdControlAV.Agent` | Main executable binary |
| `/opt/prodcontrolav/agent/.env` | Environment configuration (secure) |
| `/opt/prodcontrolav/agent/scripts/` | Deployment and service scripts |
| `/etc/systemd/system/prodcontrolav-agent.service` | Systemd service definition |

## Service Management Commands

After deployment, manage the service on the Raspberry Pi:

```bash
# Check service status
sudo systemctl status prodcontrolav-agent

# View live logs
sudo journalctl -u prodcontrolav-agent -f

# Start/stop/restart service
sudo systemctl start prodcontrolav-agent
sudo systemctl stop prodcontrolav-agent
sudo systemctl restart prodcontrolav-agent

# Enable/disable automatic startup
sudo systemctl enable prodcontrolav-agent
sudo systemctl disable prodcontrolav-agent
```

## Troubleshooting

### Common Issues and Solutions

**SSH Connection Failed**
- Verify Pi is powered on and connected
- Ensure SSH is enabled: `sudo systemctl enable ssh`
- Check SSH key authentication: `ssh pi@your-pi-ip`

**Build Failed**
- Check Docker daemon is running: `docker info`
- Try native build: `--no-docker`
- Verify .NET 8 SDK is installed

**Service Won't Start**
- Check logs: `sudo journalctl -u prodcontrolav-agent -f`
- Verify API connectivity from Pi
- Check environment file: `/opt/prodcontrolav/agent/.env`

## Integration with CI/CD

The deployment scripts can be integrated into CI/CD pipelines:

```yaml
# GitHub Actions example
- name: Deploy to Raspberry Pi
  env:
    PI_HOST: ${{ secrets.PI_HOST }}
    API_KEY: ${{ secrets.API_KEY }}
  run: |
    ./scripts/deploy-agent.sh \
      --pi-host "$PI_HOST" \
      --pi-user pi \
      --api-url https://my-server.com/api \
      --api-key "$API_KEY"
```

## Development and Testing

The solution includes a demo script to showcase functionality:

```bash
./scripts/demo.sh
```

This demonstrates parameter validation, help functionality, and error handling without requiring actual SSH connectivity.

## Files Added to Repository

- `scripts/deploy-agent.sh` - Main deployment orchestration script
- `scripts/quick-deploy.sh` - Interactive deployment helper
- `scripts/README.md` - Detailed documentation
- `scripts/demo.sh` - Demonstration script
- Updated `.gitignore` - Excludes temporary deployment files

## Benefits

✅ **Streamlined workflow** - Single command deployment
✅ **Secure by design** - No credentials in source control
✅ **Comprehensive validation** - Prevents common deployment errors
✅ **Detailed logging** - Clear visibility into deployment process
✅ **Flexible options** - Supports various deployment scenarios
✅ **Production ready** - Includes service management and monitoring
✅ **Well documented** - Comprehensive help and troubleshooting guides