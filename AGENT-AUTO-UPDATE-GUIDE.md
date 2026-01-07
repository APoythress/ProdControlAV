# Agent Auto-Update System Guide

## Overview

The ProdControlAV Agent now includes a comprehensive auto-update system with backup, rollback, and manual triggering capabilities. This guide covers how the system works, how to use it, and how to troubleshoot issues.

## Features

### Automatic Update Detection
- Checks for updates once per hour (configurable via `CheckIntervalSeconds`)
- Uses NetSparkle with Ed25519 signature verification for security
- Downloads appcast.json from Azure Blob Storage
- Compares current version with available version

### Backup and Rollback
- **Automatic backup creation** before every update
- Backup location: `/opt/prodcontrolav/agent.YYYY-MM-DD_HH-mm-ss`
- Example: `/opt/prodcontrolav/agent.2026-01-07_14-30-15`
- **Automatic rollback** if update fails during installation
- Preserves previous version for manual recovery

### Manual Update Triggering
- **Agent Health Dashboard** shows update availability
- **"Apply Update" button** allows DevAdmin users to trigger updates
- **Confirmation dialog** shows current and new versions
- **Real-time status updates** during update process

### Update Process
1. **Detection**: Agent checks appcast.json for new version
2. **Download**: Downloads signed ZIP file from Azure Blob Storage
3. **Verification**: Validates Ed25519 signature
4. **Backup**: Creates timestamped backup of current version
5. **Extract**: Extracts update to temporary directory
6. **Install**: Copies files to agent directory
7. **Restart**: Agent exits (systemd restarts it automatically)
8. **Verify**: New version logs on startup

### Rollback on Failure
- If any step fails, automatic rollback occurs
- Restores files from most recent backup
- Logs detailed error information
- Manual recovery instructions logged

## Configuration

### Agent Configuration (appsettings.json)

```json
{
  "Update": {
    "Enabled": true,
    "AppcastUrl": "https://pcavstore.blob.core.windows.net/updates/appcast.json",
    "Ed25519PublicKey": "DIL8Xjb5JdXesMk9/SpzNb3JW406nGOWXbLxtJu0OCM=",
    "CheckIntervalSeconds": 3600,
    "AutoInstall": true
  }
}
```

**Configuration Options:**
- `Enabled`: Enable/disable auto-update system
- `AppcastUrl`: URL to appcast.json manifest
- `Ed25519PublicKey`: Public key for signature verification
- `CheckIntervalSeconds`: How often to check for updates (default: 3600 = 1 hour)
- `AutoInstall`: Automatically install updates when detected (default: true)

### API Configuration (appsettings.json)

```json
{
  "Update": {
    "AppcastUrl": "https://pcavstore.blob.core.windows.net/updates/appcast.json"
  }
}
```

This allows the API to fetch the latest version for the health dashboard.

## Using the System

### Automatic Updates (AutoInstall: true)

When `AutoInstall` is enabled:
1. Agent checks for updates every hour
2. If update available, downloads and applies automatically
3. Creates backup before installation
4. Restarts automatically after successful update
5. New version logs on next startup

**Timeline:**
```
T+0:00  - New release published to Azure Blob Storage
T+1:00  - Agent detects update (next scheduled check)
T+1:01  - Backup created at /opt/prodcontrolav/agent.2026-01-07_14-00-00
T+1:02  - Update downloaded and installed
T+1:03  - Agent exits (systemd restarts it)
T+1:04  - Agent running new version
```

### Manual Updates via Dashboard

For controlled updates or when `AutoInstall` is disabled:

1. **Navigate to Agent Health Dashboard**
   - Go to `/agent-health` in web app
   - Requires DevAdmin role

2. **Check for Updates**
   - Orange badge shows updates available
   - Version number displayed: `1.0.0 → 1.0.1`

3. **Trigger Update**
   - Click "Apply Update" button
   - Confirmation dialog appears
   - Review version information
   - Click "Apply Update" to confirm

4. **Monitor Progress**
   - Button shows "Applying..." with spinner
   - Success message appears after ~2 minutes
   - Dashboard refreshes automatically

5. **Verify Update**
   - Check agent version in dashboard
   - Agent should show new version
   - Status should remain "online"

## Monitoring and Logs

### Check Update Status

```bash
# View update-related logs
sudo journalctl -u prodcontrolav-agent -f | grep -i update

# Check current version
sudo journalctl -u prodcontrolav-agent | grep "Current agent version"

# Check for update checks
sudo journalctl -u prodcontrolav-agent | grep "Checking for updates"

# Check for successful updates
sudo journalctl -u prodcontrolav-agent | grep "Update applied successfully"
```

### Expected Log Messages

**Successful Update:**
```
[INF] Checking for updates (current version: 1.0.0)...
[INF] Update available: Version 1.0.1 (current: 1.0.0)
[INF] Auto-install is enabled. Applying update...
[INF] Starting update process for version 1.0.1
[INF] Creating backup at: /opt/prodcontrolav/agent.2026-01-07_14-30-15
[INF] Backup created successfully
[INF] Downloading update to: /tmp/prodcontrolav-update-1.0.1.zip
[INF] Update downloaded successfully
[INF] Extracting update to temporary directory
[INF] Copying files from temporary directory to agent directory
[INF] Update files copied successfully
[INF] Update applied successfully. Initiating agent restart in 5 seconds...
[INF] Backup available at: /opt/prodcontrolav/agent.2026-01-07_14-30-15
[INF] Exiting agent for restart. New version: 1.0.1
```

**Manual Trigger:**
```
[INF] Manual update trigger detected, checking for updates immediately...
[INF] Update available: Version 1.0.1 (current: 1.0.0)
[INF] Manual update trigger - applying update immediately...
[INF] Starting update process for version 1.0.1
...
```

**Update Failed with Rollback:**
```
[ERR] Update failed. Attempting rollback from backup: /opt/prodcontrolav/agent.2026-01-07_14-30-15
[INF] Rolling back from backup: /opt/prodcontrolav/agent.2026-01-07_14-30-15
[INF] Rollback completed successfully from: /opt/prodcontrolav/agent.2026-01-07_14-30-15
```

## Troubleshooting

### Update Not Detected

**Check:**
1. Update service is enabled: `"Enabled": true`
2. AppcastUrl is correct and accessible
3. Network connectivity from agent to Azure
4. appcast.json has correct version number

**Debug:**
```bash
# Test appcast URL manually
curl https://pcavstore.blob.core.windows.net/updates/appcast.json

# Check agent logs for update service initialization
sudo journalctl -u prodcontrolav-agent | grep "NetSparkle update system"

# Check for errors during update check
sudo journalctl -u prodcontrolav-agent | grep -i "error.*update"
```

### Update Download Fails

**Check:**
1. Download URL in appcast.json is correct
2. ZIP file exists in Azure Blob Storage
3. Network connectivity during download
4. Disk space available (at least 200MB free)

**Debug:**
```bash
# Check available disk space
df -h /opt/prodcontrolav

# Test download manually
wget https://pcavstore.blob.core.windows.net/updates/ProdControlAV-Agent-1.0.1-linux-arm64.zip

# Check agent logs
sudo journalctl -u prodcontrolav-agent | grep "Downloading update"
```

### Signature Verification Fails

**Check:**
1. Ed25519PublicKey matches private key used to sign release
2. ZIP file hasn't been modified after signing
3. Signature in appcast.json is correct

**Debug:**
```bash
# Check logs for signature errors
sudo journalctl -u prodcontrolav-agent | grep -i "signature"

# Verify appcast has signature field
curl https://pcavstore.blob.core.windows.net/updates/appcast.json | jq '.items[0].signature'
```

### Update Applied But Version Not Changed

**Check:**
1. Agent actually restarted after update
2. Systemd service is running correct binary
3. Files were actually copied to agent directory

**Debug:**
```bash
# Check if service restarted
sudo journalctl -u prodcontrolav-agent | grep "Exiting agent for restart"

# Check file timestamps in agent directory
ls -la /opt/prodcontrolav/agent/

# Check which binary is running
ps aux | grep ProdControlAV.Agent

# Verify binary version
/opt/prodcontrolav/agent/ProdControlAV.Agent --version
```

### Manual Trigger Not Working

**Check:**
1. User has DevAdmin role
2. Agent is online
3. Update is actually available
4. Command queue is working

**Debug:**
```bash
# Check API logs for trigger request
# (API logs location depends on deployment)

# Check agent logs for UPDATE command
sudo journalctl -u prodcontrolav-agent | grep "Received UPDATE command"

# Check for update trigger signal file
ls -la /tmp/prodcontrolav-update-trigger

# Check command queue
# (depends on your command queue implementation)
```

### Rollback Occurred

**Investigate:**
```bash
# Find rollback logs
sudo journalctl -u prodcontrolav-agent | grep -i rollback

# Check error that triggered rollback
sudo journalctl -u prodcontrolav-agent | grep "Update failed"

# View full error context
sudo journalctl -u prodcontrolav-agent --since "10 minutes ago"
```

**Recovery:**
If automatic rollback failed, manual recovery:
```bash
# Stop agent
sudo systemctl stop prodcontrolav-agent

# Find most recent backup
ls -la /opt/prodcontrolav/ | grep agent.

# Restore from backup
cd /opt/prodcontrolav
sudo rm -rf agent/*
sudo cp -r agent.2026-01-07_14-30-15/* agent/

# Restart agent
sudo systemctl start prodcontrolav-agent

# Verify version
sudo journalctl -u prodcontrolav-agent | grep "Current agent version"
```

## Manual Update Procedure (Without Auto-Update)

If you need to update manually without using the auto-update system:

1. **Download Update**
   ```bash
   cd /tmp
   wget https://pcavstore.blob.core.windows.net/updates/ProdControlAV-Agent-1.0.1-linux-arm64.zip
   ```

2. **Create Manual Backup**
   ```bash
   BACKUP_DIR="/opt/prodcontrolav/agent.$(date +%Y-%m-%d_%H-%M-%S)"
   sudo mkdir -p "$BACKUP_DIR"
   sudo cp -r /opt/prodcontrolav/agent/* "$BACKUP_DIR/"
   echo "Backup created at: $BACKUP_DIR"
   ```

3. **Stop Agent**
   ```bash
   sudo systemctl stop prodcontrolav-agent
   ```

4. **Extract and Install**
   ```bash
   unzip ProdControlAV-Agent-1.0.1-linux-arm64.zip -d agent-new
   sudo cp -r agent-new/* /opt/prodcontrolav/agent/
   ```

5. **Restart Agent**
   ```bash
   sudo systemctl start prodcontrolav-agent
   ```

6. **Verify**
   ```bash
   sudo journalctl -u prodcontrolav-agent | grep "Current agent version"
   ```

## Backup Management

### List Backups
```bash
ls -lah /opt/prodcontrolav/ | grep agent.
```

### Check Backup Size
```bash
du -sh /opt/prodcontrolav/agent.*
```

### Clean Old Backups
Keep only the last 5 backups to save disk space:
```bash
cd /opt/prodcontrolav
# List backups sorted by date (oldest first)
ls -dt agent.* | tail -n +6 | xargs -r sudo rm -rf
```

### Restore Specific Backup
```bash
# Stop agent
sudo systemctl stop prodcontrolav-agent

# Restore from specific backup
sudo rm -rf /opt/prodcontrolav/agent/*
sudo cp -r /opt/prodcontrolav/agent.2026-01-07_14-30-15/* /opt/prodcontrolav/agent/

# Restart agent
sudo systemctl start prodcontrolav-agent
```

## Best Practices

### For Development/Staging
- Keep `AutoInstall: false` during testing
- Use manual trigger from dashboard for controlled updates
- Test updates on a single agent before rolling out
- Keep multiple backups during testing phase

### For Production
- Enable `AutoInstall: true` for hands-free updates
- Monitor agent health dashboard for update status
- Set up alerts for failed updates (via monitoring system)
- Keep at least 3-5 recent backups
- Schedule updates during low-traffic periods if using manual trigger

### Release Strategy
1. **Create Release** - Build and sign new version
2. **Upload to Staging** - Test in staging environment first
3. **Monitor Staging** - Verify update works correctly
4. **Upload to Production** - Release to production blob storage
5. **Monitor Production** - Watch for update success/failures
6. **Rollback if Needed** - Remove from appcast or trigger manual rollback

## Security Considerations

### Signature Verification
- All updates MUST be signed with Ed25519 private key
- Agent verifies signature before installation
- NetSparkle uses `SecurityMode.Strict` for maximum security
- Invalid signatures cause update rejection

### Backup Security
- Backups contain full agent binaries
- Keep backup directory restricted: `chmod 700 /opt/prodcontrolav`
- Don't store sensitive keys in backups
- Regularly clean old backups to prevent disk fill

### Manual Trigger Authorization
- Only DevAdmin users can trigger updates
- API validates tenant ownership before triggering
- All trigger actions logged for audit
- Command queue ensures delivery to correct agent

## Appendix

### Systemd Configuration for Auto-Restart

Ensure systemd will restart agent after update:

```ini
[Service]
Restart=always
RestartSec=10
```

Verify configuration:
```bash
systemctl cat prodcontrolav-agent | grep Restart
```

### Version Numbering
- Format: `Major.Minor.Patch` (e.g., `1.0.1`)
- Follows semantic versioning
- Git hash appended in logs: `1.0.1+abc123def`
- Health dashboard displays clean version: `1.0.1`

### Files Modified During Update
- All files in `/opt/prodcontrolav/agent/` directory
- DLL files, configuration, executables
- Does NOT modify:
  - `/etc/systemd/system/prodcontrolav-agent.service`
  - `/opt/prodcontrolav/agent.*` (backups)
  - Environment variables or systemd settings

---

**Last Updated:** 2026-01-07  
**Document Version:** 1.0  
**Supported Agent Versions:** 1.0.0+
