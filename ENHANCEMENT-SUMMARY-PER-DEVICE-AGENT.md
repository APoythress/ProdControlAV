# Device Notifications Enhancement - Per-Device Controls & Agent Monitoring

## Summary

Enhanced the device notifications feature with two major improvements based on user feedback:

1. **Per-Device SMS Alert Controls** - Users can now enable/disable SMS notifications for individual devices
2. **Agent Offline Detection** - System monitors agent heartbeat and alerts if agent goes offline for 20+ minutes

## Changes Made

### 1. Per-Device SMS Alert Toggle

**Problem:** Some devices are intentionally offline for extended periods (e.g., weekends, maintenance), causing unnecessary alerts.

**Solution:** Added per-device SMS alert control so users can disable notifications for specific devices while keeping them enabled for critical equipment.

**Implementation:**
- Added `SmsAlertsEnabled` boolean field to `Device` model (defaults to `true`)
- Updated `DeviceOfflineNotificationService` to check device SMS settings before sending notifications
- Added SMS Alerts toggle column to Device Settings UI page
- Updated API endpoints to handle the new field in create/update operations
- Database migration: `AddPerDeviceSmsAlertsAndAgentNotifications`

**UI Changes:**
- Device Settings page now has a new "SMS Alerts" column with toggle switches
- Users can enable/disable alerts per device alongside ping frequency settings
- Changes save when user clicks the "Save" button for that device

**Code Changes:**
```csharp
// Device.cs
public bool SmsAlertsEnabled { get; set; } = true; // Default enabled

// DeviceOfflineNotificationService.cs
if (!deviceInfo.SmsAlertsEnabled)
{
    _logger.LogDebug("Skipping device {DeviceId} - SMS alerts disabled", deviceId);
    continue;
}
```

### 2. Agent Offline Detection

**Problem:** If the monitoring agent itself goes offline, no device status updates occur, but users aren't notified about the agent being down.

**Solution:** Monitor agent heartbeat (LastSeenUtc) and send SMS notification if agent hasn't checked in for 20+ minutes.

**Implementation:**
- Extended `DeviceOfflineNotificationService` to monitor agent status from Table Storage
- Added `CheckTenantAgentsAsync` method to check all agents for each tenant
- Tracks agent LastSeenUtc state to detect when agent becomes unresponsive
- Uses 12-hour time format in notification as requested: "02:45:30 PM"
- Rate-limited (60-minute cooldown) to prevent notification spam
- Leverages existing Pro plan + SMS opt-in infrastructure

**Notification Format:**
```
PROD-CONTROL: Alert - {agentName} is offline! Last seen: 02:45:30 PM
```

**Code Changes:**
```csharp
// DeviceOfflineNotificationService.cs
private const int AgentOfflineThresholdMinutes = 20;
private readonly ConcurrentDictionary<Guid, DateTimeOffset?> _lastKnownAgentSeen = new();

// Format time in 12-hour format
var lastSeenFormatted = currentLastSeen.Value.ToLocalTime().ToString("hh:mm:ss tt");
var message = $"PROD-CONTROL: Alert - {agent.Name} is offline! Last seen: {lastSeenFormatted}";
```

**Detection Logic:**
- Queries `IAgentAuthStore.GetAgentsForTenantAsync()` to get all agents
- Checks if `LastSeenUtc` is more than 20 minutes old
- Tracks previous LastSeen state to avoid duplicate notifications
- Only notifies on state transitions (agent becomes offline)
- Respects rate limiting (won't spam if agent stays offline)

## Technical Details

### Database Schema Changes
- **Device.SmsAlertsEnabled** - boolean, default true, allows per-device SMS control
- Migration: `20260201143747_AddPerDeviceSmsAlertsAndAgentNotifications.cs`

### Background Service Updates
- `DeviceOfflineNotificationService` now monitors both devices and agents
- Two concurrent dictionaries for state tracking:
  - `_lastKnownStatus` - Device online/offline state
  - `_lastKnownAgentSeen` - Agent LastSeenUtc timestamps
- Polling interval: 30 seconds (unchanged)
- Agent offline threshold: 20 minutes
- Rate limiting: 60 minutes for both devices and agents

### UI Updates
- Device Settings page (`Settings.razor`):
  - Added "SMS Alerts" column with toggle switches
  - Toggle uses Bootstrap form-switch component
  - Visually integrated with existing ping frequency controls

### API Updates
- `DevicesController.UpsertDevice` DTO now includes `SmsAlertsEnabled` field
- Create operation defaults `SmsAlertsEnabled` to true
- Update operation respects user-provided value

## Testing

### Existing Tests
All existing unit tests continue to pass (11/11):
- 4 tests for `TwilioSmsService`
- 7 tests for `UserPlanController`

### Manual Testing Scenarios

**Per-Device SMS Alerts:**
1. Navigate to Device Settings page
2. Toggle SMS Alerts off for a device
3. Click Save
4. Stop agent to trigger device offline
5. Verify no SMS sent for that device
6. Toggle SMS Alerts back on
7. Verify SMS sent when device goes offline again

**Agent Offline Detection:**
1. Stop the agent service
2. Wait 20+ minutes
3. Verify SMS notification sent with agent name and last seen time in 12-hour format
4. Restart agent
5. Verify no duplicate notification (rate limiting works)
6. Stop agent again after 60+ minutes
7. Verify new notification sent

## Benefits

### User Experience
- **Granular Control**: Users can customize notifications per device
- **Reduced Noise**: No alerts for intentionally offline devices
- **Agent Awareness**: Users know when monitoring itself is down
- **Clear Timestamps**: 12-hour format is easier to read

### Operational
- **No Breaking Changes**: Existing functionality preserved
- **Backward Compatible**: All existing devices default to SMS enabled
- **Rate Limited**: Prevents SMS spam even if agent flaps
- **Efficient**: Leverages existing Table Storage infrastructure

## Files Modified

1. `src/ProdControlAV.Core/Models/Device.cs` - Added SmsAlertsEnabled field
2. `src/ProdControlAV.API/Services/DeviceOfflineNotificationService.cs` - Added agent monitoring
3. `src/ProdControlAV.API/Controllers/DevicesController.cs` - Updated DTO and handlers
4. `src/ProdControlAV.WebApp/Pages/Settings.razor` - Added SMS Alerts column
5. `src/ProdControlAV.WebApp/Controllers/DeviceApiClient.cs` - Updated DTO
6. `src/ProdControlAV.API/Migrations/` - New migration file

## Future Enhancements

Potential improvements for future iterations:
1. **Agent Health Dashboard** - UI page showing agent status across all tenants
2. **Customizable Thresholds** - Allow users to configure agent offline threshold
3. **Email Notifications** - Provide email alternative to SMS for agent alerts
4. **Agent Recovery Notifications** - Notify when agent comes back online
5. **Bulk SMS Toggle** - Enable/disable SMS for multiple devices at once

## Configuration

No additional configuration required. The enhancements use existing infrastructure:
- Twilio SMS service (already configured)
- Azure Table Storage (already in use)
- Pro plan + SMS opt-in (already implemented)

## Deployment Notes

1. Apply database migration: `dotnet ef database update`
2. Restart API service to load new background service logic
3. All existing devices will have SMS alerts enabled by default
4. Users can selectively disable alerts via Device Settings page

## Commit

Changes committed in: `2b5d3f4` - "Add per-device SMS alerts toggle and agent offline detection"
