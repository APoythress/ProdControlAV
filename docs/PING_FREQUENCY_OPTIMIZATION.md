# Ping Frequency Optimization Feature

## Overview

This feature allows users to configure individual ping frequencies for each device in the system, reducing unnecessary database operations and lowering Azure SQL DB vCore costs. Instead of using a single global ping interval for all devices, users can now set different frequencies based on the importance and monitoring requirements of each device.

## Key Benefits

1. **Cost Optimization**: Reduce Azure SQL DB vCore costs by pinging less critical devices less frequently
2. **Flexible Monitoring**: Configure high-priority devices for frequent monitoring (e.g., 5 seconds) and low-priority devices for less frequent checks (e.g., 5 minutes)
3. **Load Balancing**: Spread out database writes by staggering device ping frequencies
4. **Customizable**: Each device can have its own monitoring schedule

## Configuration

### Via Web Interface

1. Navigate to the **Settings** page from the menu bar
2. View the table of all devices with their current ping frequencies
3. Adjust the ping frequency for any device (minimum: 5 seconds, maximum: 3600 seconds / 1 hour)
4. Click **Save** to apply the changes

The agent will automatically pick up the new frequency settings on its next device refresh cycle (typically every 30 seconds).

### Default Settings

- **New Devices**: Default ping frequency is **10 seconds**
- **Minimum Frequency**: 5 seconds (prevents excessive polling)
- **Maximum Frequency**: 3600 seconds (1 hour)

## How It Works

### Backend Implementation

1. **Device Model**: Added `PingFrequencySeconds` property to store per-device frequency
2. **Agent Service**: Modified polling loop to:
   - Track last ping time for each device
   - Compare elapsed time against device-specific frequency
   - Only ping devices when their interval has elapsed
3. **API Endpoint**: Updated device endpoints to support reading and updating ping frequencies

### Agent Behavior

The agent runs a main polling loop every `IntervalMs` milliseconds (default: 10 seconds). On each cycle:

1. Retrieves all devices from the database
2. Checks each device's last ping time
3. Filters devices that need pinging based on their `PingFrequencySeconds`
4. Pings only the filtered devices
5. Updates last ping time after each successful ping

**Example**: If a device has `PingFrequencySeconds = 60`:
- The agent checks the device on every cycle (every 10 seconds)
- But only pings it if 60+ seconds have elapsed since the last ping
- This reduces database writes by ~83% compared to pinging every 10 seconds

## Cost Savings Example

### Before Optimization
- 50 devices × 6 pings/minute = 300 database operations/minute
- 300 × 60 × 24 = 432,000 operations/day

### After Optimization (mixed frequencies)
- 10 critical devices at 10s frequency: 10 × 6 = 60 ops/min
- 20 standard devices at 30s frequency: 20 × 2 = 40 ops/min  
- 20 low-priority devices at 300s frequency: 20 × 0.2 = 4 ops/min
- **Total**: 104 operations/minute (65% reduction)
- 104 × 60 × 24 = 149,760 operations/day

**Savings**: 282,240 database operations per day (~65% reduction in write operations)

## API Reference

### Get Device List
```http
GET /api/devices/devices
Authorization: Bearer {token}
```

Response includes `pingFrequencySeconds` for each device:
```json
[
  {
    "id": "guid",
    "name": "Camera 1",
    "ip": "192.168.1.100",
    "pingFrequencySeconds": 10
  }
]
```

### Update Device Ping Frequency
```http
PUT /api/devices/{id}
Authorization: Bearer {token}
Content-Type: application/json

{
  "name": "Camera 1",
  "ip": "192.168.1.100",
  "pingFrequencySeconds": 30
}
```

## Recommendations

### Suggested Frequencies by Device Type

- **Critical Devices** (live production equipment): 5-10 seconds
- **Important Devices** (backup/standby equipment): 30-60 seconds
- **Standard Devices** (general equipment): 60-120 seconds
- **Low Priority** (testing/development equipment): 300-600 seconds

### Best Practices

1. Start with default 10-second frequency for all devices
2. Monitor usage patterns and identify less critical devices
3. Gradually increase frequency for non-critical devices
4. Keep at least a few devices at high frequency for alerting
5. Review and adjust frequencies quarterly based on operational needs

## Troubleshooting

### Device Not Being Pinged
- Check that `PingFrequencySeconds` is not set too high
- Verify agent is running and connected to API
- Check agent logs for errors

### Changes Not Taking Effect
- Agent refreshes device list every 30 seconds (configurable)
- Wait 30-60 seconds after changing frequency
- Restart agent if changes still not applied

### Performance Issues
- If agent logs show "No devices need pinging", frequencies may be too high
- Ensure at least some devices have frequencies ≤ `IntervalMs` setting
- Check that agent's `IntervalMs` configuration is appropriate (default: 10000ms)

## Future Enhancements

Potential improvements for future versions:

1. **Bulk Update**: Update frequencies for multiple devices at once
2. **Frequency Profiles**: Pre-defined frequency sets (e.g., "Critical", "Standard", "Low")
3. **Dynamic Adjustment**: Auto-adjust frequencies based on device stability
4. **Usage Analytics**: Dashboard showing ping statistics and cost estimates
5. **Schedule-Based**: Different frequencies for business hours vs. off-hours
