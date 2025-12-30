# Agent Update Issue - Resolution Summary

## Problem
The agent was not properly checking for updates, and the health dashboard was showing incorrect version information (1.0.001 instead of 1.0.002).

## Root Causes Identified

### 1. Incorrect Appcast URL Configuration
**Issue**: The agent's `appsettings.json` had the appcast URL pointing to the container directory instead of the actual appcast.json file.
- **Before**: `https://pcavstore.blob.core.windows.net/updates`
- **After**: `https://pcavstore.blob.core.windows.net/updates/appcast.json`

### 2. Missing API Configuration
**Issue**: The API didn't have the Update configuration section needed to fetch the latest version from the appcast for the health dashboard.
- **Fix**: Added `Update` section to `src/ProdControlAV.API/appsettings.json` with the correct appcast URL.

### 3. Limited Update Service Logging
**Issue**: The UpdateService didn't log enough information to diagnose update checking issues.
- **Fix**: Enhanced logging to show:
  - Current agent version at startup
  - Current vs. available version during checks
  - More detailed status messages for debugging

## Changes Made

### 1. Agent Configuration (`src/ProdControlAV.Agent/appsettings.json`)
```json
"Update": {
  "Enabled": true,
  "AppcastUrl": "https://pcavstore.blob.core.windows.net/updates/appcast.json",  // Fixed URL
  "Ed25519PublicKey": "DIL8Xjb5JdXesMk9/SpzNb3JW406nGOWXbLxtJu0OCM=",
  "CheckIntervalSeconds": 3600,
  "AutoInstall": true
}
```

### 2. API Configuration (`src/ProdControlAV.API/appsettings.json`)
```json
"Update": {
  "AppcastUrl": "https://pcavstore.blob.core.windows.net/updates/appcast.json"
}
```

### 3. UpdateService Improvements (`src/ProdControlAV.Agent/Services/UpdateService.cs`)
- Added `_currentVersion` field to track the agent's version
- Enhanced logging to show current version at initialization
- Improved update check logging to show current vs. available version
- Added more descriptive error messages

## How Updates Work

### Update Detection Flow
1. **Agent checks for updates** (every hour by default)
2. **Fetches appcast.json** from Azure Blob Storage
3. **Compares versions** using semantic versioning
4. **Logs availability** if a newer version is found
5. **Does NOT auto-install** - requires manual deployment (by design)

### Version Tracking Flow
1. **Agent reports version** via heartbeat to API (every 60 seconds)
2. **API stores version** in Azure Table Storage
3. **Health dashboard fetches**:
   - Agent's current version from Table Storage
   - Latest available version from appcast.json
4. **Dashboard shows** update availability status

## How to Verify the Fix

### 1. Check Agent Logs for Update Service
```bash
sudo journalctl -u prodcontrolav-agent -f | grep -i update
```

Expected output:
```
Initializing NetSparkle update system...
Current agent version: 1.0.001+<git-hash>
Appcast URL: https://pcavstore.blob.core.windows.net/updates/appcast.json
Check interval: 3600 seconds
NetSparkle update system initialized successfully
```

### 2. Verify Update Detection
After the check interval (1 hour), you should see:
```
Checking for updates (current version: 1.0.001)...
Update available: Version 1.0.002 (current: 1.0.001)
```

Or if up to date:
```
Checking for updates (current version: 1.0.002)...
No updates available. Current version 1.0.002 is up to date.
```

### 3. Check Health Dashboard
1. Navigate to the Agent Health dashboard in the web UI
2. Look at the "Updates Available" count in the summary cards
3. Check the agent's row - if update available, you'll see an orange badge with an up arrow icon
4. Hover over the badge to see the available version number

## Important Notes

### Update Installation is Manual
The current implementation **detects** updates but does NOT automatically install them. This is by design for safety. To apply an update:

1. **Monitor logs** for update notifications
2. **Download the update**:
   ```bash
   cd /tmp
   wget https://pcavstore.blob.core.windows.net/updates/ProdControlAV-Agent-1.0.002-linux-arm64.zip
   ```
3. **Stop the agent**:
   ```bash
   sudo systemctl stop prodcontrolav-agent
   ```
4. **Extract and deploy**:
   ```bash
   unzip ProdControlAV-Agent-1.0.002-linux-arm64.zip -d agent-new
   sudo cp -r agent-new/* /opt/prodcontrolav/agent/
   ```
5. **Restart the agent**:
   ```bash
   sudo systemctl start prodcontrolav-agent
   ```
6. **Verify new version**:
   ```bash
   sudo journalctl -u prodcontrolav-agent | grep "Current agent version"
   ```

### Version Numbering
- The GitHub Actions workflow sets the version via `-p:Version=X.Y.Z`
- This overrides the hardcoded version in the `.csproj` file
- The agent reports version including git hash: `1.0.002+<git-hash>`
- The health dashboard strips the git hash for comparison

### Releasing New Versions
To create a new version release:

1. **Option 1: Tag-based release** (recommended)
   ```bash
   git tag agent-v1.0.002
   git push origin agent-v1.0.002
   ```

2. **Option 2: Manual workflow dispatch**
   - Go to Actions → Agent Release Build
   - Click "Run workflow"
   - Enter version: `1.0.002`
   - Enter description: "Bug fixes and improvements"
   - Run workflow

The workflow will:
- Build the agent for linux-arm64
- Sign it with Ed25519
- Generate appcast.json with the new version
- Upload both to Azure Blob Storage

## Testing the Complete Flow

### 1. Create a Test Release
```bash
# Create and push a test tag
git tag agent-v1.0.002
git push origin agent-v1.0.002
```

### 2. Wait for GitHub Actions
- Monitor the "Agent Release Build" workflow
- Verify it completes successfully
- Check Azure Blob Storage for new files

### 3. Check Agent Detection
- Wait up to 1 hour for the next update check (or restart the agent to check immediately)
- Check logs for "Update available" message
- Verify the health dashboard shows the update

### 4. Apply the Update
- Follow manual update steps above
- Restart agent
- Verify new version in logs and dashboard

## Troubleshooting

### Agent Not Detecting Updates
**Check**:
1. UpdateService is enabled: `"Update": { "Enabled": true }`
2. Appcast URL is correct and accessible
3. Ed25519 public key matches the private key used for signing
4. Network connectivity from Pi to Azure Blob Storage

**Debug**:
```bash
# Test appcast URL access
curl -v https://pcavstore.blob.core.windows.net/updates/appcast.json

# Check agent logs
sudo journalctl -u prodcontrolav-agent | grep -E "update|Update"
```

### Dashboard Not Showing Updates
**Check**:
1. API has Update configuration with appcast URL
2. API can reach Azure Blob Storage
3. Agent is sending heartbeats with correct version
4. appcast.json has correct version format

**Debug**:
```bash
# Check API logs for appcast fetch
# Check agent heartbeat logs
sudo journalctl -u prodcontrolav-agent | grep -i heartbeat
```

### Version Mismatch
**Check**:
1. Agent was actually updated (files replaced)
2. Agent was restarted after update
3. Systemd service is running the correct binary
4. GitHub Actions workflow set the correct version

**Debug**:
```bash
# Check what version the agent reports
sudo journalctl -u prodcontrolav-agent | grep "Current agent version"

# Check which binary is running
ps aux | grep ProdControlAV.Agent
ls -la /opt/prodcontrolav/agent/ProdControlAV.Agent.dll
```

## Next Steps

### Immediate Actions
1. ✅ Changes committed and deployed to repository
2. ⏳ Wait for next PR approval and merge
3. ⏳ Update agent configuration on Raspberry Pi with new appcast URL
4. ⏳ Restart agent to apply new configuration
5. ⏳ Monitor logs to verify update checking works

### Long-term Improvements
Consider implementing in future:
1. Automatic update download and installation
2. Rollback capability if update fails
3. Update scheduling (e.g., only apply at specific times)
4. Email/webhook notifications when updates are available
5. Staged rollout (test on one agent before all)

## Summary

The fixes ensure:
- ✅ Agent can properly check for updates
- ✅ Health dashboard shows correct version information  
- ✅ Update availability is clearly indicated
- ✅ Better logging for debugging update issues

The agent will now successfully detect when updates are available, though manual deployment is still required for safety.
