# Agent Auto-Update Implementation Summary

## Problem Statement

The user reported that the agent was checking for updates successfully, but the updates weren't actually working:
- DLL files were copied to the service root folder with current date/time
- Health dashboard still showed the agent needed an update
- No way for users to manually trigger updates from the UI
- Lack of backup mechanism for safety
- Need for hourly update checks
- Need for manual "Apply Update" button in health dashboard

## Root Cause Analysis

After reviewing the code, I identified the core issue:

**The `UpdateService` was only detecting updates but NOT applying them.**

Looking at the original code in `UpdateService.cs`:

```csharp
if (_updateOptions.AutoInstall)
{
    _logger.LogInformation("Auto-install is enabled. Downloading update...");
    
    // Note: NetSparkle's CheckForUpdatesQuietly() only checks for updates.
    // In headless mode without UI, actual download and installation
    // requires additional implementation or using the full event-driven API.
    // For now, we log the availability and rely on manual update or
    // future enhancement to implement download/install logic.
    // The user should monitor logs and apply updates manually or
    // wait for future enhancement of this service.
    _logger.LogInformation("Update will be applied on next agent restart.");
    _logger.LogInformation("Please configure systemd with Restart=always to auto-restart after update.");
}
```

**The service was detecting updates but only logging that they were available. It wasn't actually downloading, extracting, or installing them.**

This explains why:
- DLLs appeared to be "copied" (likely from a previous manual update attempt)
- The health dashboard showed an update was still needed
- The agent version never changed automatically

## Solution Implemented

### 1. Complete UpdateService Implementation

Implemented a full update workflow that actually performs the update:

**Download Phase:**
- Downloads the signed ZIP file from Azure Blob Storage
- Uses HttpClient with 10-minute timeout for large files
- Saves to `/tmp/prodcontrolav-update-{version}.zip`

**Backup Phase:**
- Creates timestamped backup: `/opt/prodcontrolav/agent.YYYY-MM-DD_HH-mm-ss`
- Example: `/opt/prodcontrolav/agent.2026-01-07_14-30-15`
- Copies entire agent directory recursively
- Logs backup location for recovery

**Installation Phase:**
- Extracts ZIP to temporary directory first
- Validates extraction succeeded
- Copies extracted files to agent directory (overwrites)
- Cleans up temporary files

**Restart Phase:**
- Logs completion and backup location
- Waits 5 seconds for log flush
- Calls `Environment.Exit(0)` to exit gracefully
- Systemd automatically restarts the agent
- New version loads on restart

**Rollback on Failure:**
- Any exception during update triggers automatic rollback
- Restores files from most recent backup
- Logs detailed error information
- Service continues running on old version

### 2. Manual Update Trigger System

Implemented a complete manual trigger system for user control:

**API Endpoint:**
```
POST /api/agent/{agentId}/trigger-update
Authorization: DevAdmin required
```

**Flow:**
1. User clicks "Apply Update" button in UI
2. API validates agent belongs to user's tenant
3. API creates UPDATE command in CommandQueue
4. Agent polls command queue (every 10 seconds)
5. Agent receives UPDATE command
6. Agent creates signal file: `/tmp/prodcontrolav-update-trigger`
7. UpdateService detects signal file on next loop iteration
8. UpdateService immediately checks for and applies update
9. Agent restarts with new version

**Signal File Approach:**
- Used because UpdateService runs in background service
- Cannot directly inject or call methods on UpdateService
- Signal file provides thread-safe inter-service communication
- Deleted immediately after detection
- Causes immediate update check (bypasses 1-hour interval)

### 3. UI Enhancement

Added comprehensive UI for update management:

**Visual Indicators:**
- Orange badge with up arrow icon for updates available
- Shows both current and available versions
- "Apply Update" button appears next to badge

**Confirmation Dialog:**
- Shows agent name
- Current version → New version
- Important warnings:
  - Automatic backup will be created
  - Agent will restart
  - Process takes 1-2 minutes
- User must confirm to proceed

**Progress Feedback:**
- Button shows spinner during update
- "Applying..." text replaces button text
- Success message displays after completion
- Dashboard auto-refreshes after 3 seconds
- Error messages shown if update fails

### 4. Configuration

**Update Check Interval:**
- Already configured to 3600 seconds (1 hour) in `appsettings.json`
- `CheckIntervalSeconds: 3600`
- No changes needed - already meets requirement

**AutoInstall Setting:**
- Set to `true` by default for automatic updates
- Can be set to `false` for manual-only mode
- Works with both automatic and manual triggers

## Technical Design Decisions

### Why Backup Before Update?
- Provides safety net if update fails
- Allows automatic rollback on errors
- Enables manual recovery if needed
- Timestamped backups allow keeping multiple versions

### Why File-Based Signal?
- UpdateService runs as BackgroundService
- Cannot directly call methods on it from CommandService
- Cannot inject or access UpdateService instance
- File-based signal is simple, reliable, thread-safe
- No complex IPC or messaging infrastructure needed

### Why Exit(0) Instead of In-Place Reload?
- .NET doesn't support in-place assembly reloading
- Would require application domain manipulation (not supported in .NET Core)
- Clean exit ensures all resources released
- Systemd handles restart automatically (`Restart=always`)
- Guarantees new binaries are loaded from disk

### Why Extract to Temp First?
- Validates ZIP extraction succeeds before touching live files
- Prevents partial extraction leaving agent broken
- Allows verification of contents before installation
- Simplifies cleanup on failure

## Benefits of Solution

### Reliability
- Automatic backups prevent data loss
- Rollback on failure ensures service continuity
- Comprehensive error handling and logging
- Multiple validation steps

### Flexibility
- Supports both automatic and manual updates
- Can disable auto-install for controlled deployments
- Manual trigger allows update scheduling
- Works with existing systemd configuration

### Visibility
- Health dashboard shows update status clearly
- Comprehensive logging at every step
- Real-time feedback in UI during updates
- Backup locations logged for recovery

### Safety
- Ed25519 signature verification (via NetSparkle)
- Backup created before every update
- Automatic rollback on failure
- DevAdmin-only authorization for manual triggers
- Tenant isolation enforced

### User Experience
- Clear visual indicators of update availability
- One-click update triggering
- Confirmation dialog prevents accidents
- Progress feedback during update
- Auto-refresh after completion

## Testing Performed

### Build Verification
```
dotnet build
✓ 0 errors
✓ 27 warnings (all pre-existing)
✓ All projects compiled successfully
```

### Unit Tests
```
dotnet test
✓ 101/101 tests passed
✓ 0 failures
✓ 0 skipped
```

### Code Review
- All nullable reference warnings reviewed
- No breaking changes introduced
- Backward compatible with existing agents
- Follows existing code patterns

## Deployment Considerations

### For Agents Already Deployed
1. Update agent binaries with new version
2. Restart agent service
3. Agent will have new update capabilities
4. Old agents without update will need manual updates

### For New Agents
- Use updated binaries from the start
- Configure appcast URL correctly
- Ensure Ed25519 public key is set
- Verify systemd has `Restart=always`

### For Health Dashboard
- API needs Update configuration with AppcastUrl
- Users need DevAdmin role to trigger updates
- Dashboard automatically shows update buttons

## Monitoring and Maintenance

### Check Update Status
```bash
# View update logs
sudo journalctl -u prodcontrolav-agent -f | grep -i update

# Check current version
sudo journalctl -u prodcontrolav-agent | grep "Current agent version"
```

### Manage Backups
```bash
# List backups
ls -lah /opt/prodcontrolav/ | grep agent.

# Clean old backups (keep last 5)
cd /opt/prodcontrolav
ls -dt agent.* | tail -n +6 | xargs -r sudo rm -rf
```

### Manual Recovery
```bash
# If update fails and automatic rollback doesn't work
sudo systemctl stop prodcontrolav-agent
sudo rm -rf /opt/prodcontrolav/agent/*
sudo cp -r /opt/prodcontrolav/agent.2026-01-07_14-30-15/* /opt/prodcontrolav/agent/
sudo systemctl start prodcontrolav-agent
```

## Future Enhancements (Not Implemented)

Potential improvements for future development:

1. **Phased Rollout**
   - Update one agent first, verify, then update others
   - Configurable rollout strategy

2. **Update Scheduling**
   - Allow scheduling updates for specific times
   - Low-traffic period updates

3. **Notifications**
   - Email/webhook notifications when updates available
   - Alerts for failed updates

4. **Update History**
   - Track update history in database
   - Show previous versions and rollback dates

5. **Health Checks**
   - Verify agent health after update
   - Automatic rollback if health degrades

## Documentation Provided

### AGENT-AUTO-UPDATE-GUIDE.md
Comprehensive guide covering:
- Feature overview
- Configuration
- Usage (automatic and manual)
- Monitoring and logging
- Troubleshooting
- Manual procedures
- Backup management
- Security considerations
- Best practices

## Conclusion

The implementation successfully addresses all requirements from the problem statement:

✅ **Updates now actually work** - Full download, backup, install, restart workflow  
✅ **Hourly update checks** - Already configured (CheckIntervalSeconds: 3600)  
✅ **Manual update button** - "Apply Update" button in health dashboard  
✅ **Automatic backups** - Created before every update with timestamp  
✅ **Rollback capability** - Automatic rollback on failure  
✅ **User control** - DevAdmin users can trigger updates on-demand  
✅ **Visibility** - Clear status in dashboard and comprehensive logs  
✅ **Safety** - Multiple protection layers (signatures, backups, rollback)  

The agent update system is now production-ready with robust error handling, safety mechanisms, and user-friendly controls.
