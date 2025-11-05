# Idle Detection and SQL Activity Suspension

This document describes the idle detection and SQL activity suspension feature implemented to reduce database costs during periods of inactivity.

## Overview

The system now monitors user and agent activity and automatically suspends non-critical background SQL operations when the system is idle. This allows the SQL database to truly idle and potentially scale down to zero during extended periods of no activity.

## Architecture

### Components

1. **IActivityMonitor Interface** (`ProdControlAV.Core`)
   - Defines the contract for tracking system activity
   - Records user and agent activity timestamps
   - Determines when the system is idle

2. **DistributedActivityMonitor** (`ProdControlAV.Infrastructure`)
   - Implements activity monitoring using Azure Table Storage
   - Coordinates activity tracking across multiple API and agent instances
   - Stores activity in the `SystemActivity` table

3. **ActivityMonitorOptions** (`ProdControlAV.Core`)
   - Configuration for idle detection behavior
   - Default idle timeout: 10 minutes
   - Can be disabled entirely if needed

### Activity Tracking

Activity is recorded in the following scenarios:

**User Activity:**
- User login (`AuthController.Login`)
- Any authenticated API request (future enhancement)

**Agent Activity:**
- Agent authentication (`AgentsController.Authenticate`)
- Agent heartbeat (`AgentsController.Heartbeat`)
- Status updates and command polling

### Background Service Suspension

When the system is idle, the following background services suspend SQL operations:

1. **DeviceProjectionHostedService**
   - Polls outbox every 10 seconds normally
   - Skips SQL queries when system is idle
   - Resumes immediately when activity detected

2. **AgentService** (future)
   - Will reduce ping frequency or suspend during idle
   - Will throttle heartbeat and command polling

## Configuration

### appsettings.json (API)

```json
{
  "ActivityMonitor": {
    "IdleTimeoutMinutes": 10,
    "CheckIntervalSeconds": 30,
    "EnableIdleSuspension": true,
    "CriticalOperations": ["Alarms", "Alerts"]
  }
}
```

### Options

- **IdleTimeoutMinutes**: How long to wait with no activity before considering system idle (default: 10)
- **CheckIntervalSeconds**: How often background services check idle status (default: 30)
- **EnableIdleSuspension**: Master switch to enable/disable the feature (default: true)
- **CriticalOperations**: Operations that bypass idle suspension (e.g., alarms, alerts)

## Fail-Safe Design

The system uses a fail-safe design philosophy:

- If activity monitoring fails, the system assumes it is **active** (not idle)
- Background services continue running if they cannot determine idle status
- SQL operations are never blocked, only deferred during idle periods
- Critical operations always execute regardless of idle state

## Database Schema

### SystemActivity Table (Azure Table Storage)

| Property | Type | Description |
|----------|------|-------------|
| PartitionKey | string | Always "Activity" |
| RowKey | string | "User-{tenantId}-{userId}" or "Agent-{tenantId}-{agentId}" |
| Type | string | "User" or "Agent" |
| TenantId | string | Tenant identifier |
| UserId/AgentId | string | User or agent identifier |
| LastActivityUtc | DateTimeOffset | Timestamp of last activity |
| Timestamp | DateTimeOffset | Azure Table automatic timestamp |

## Monitoring and Metrics

### Log Messages

**System Idle:**
```
System is idle - last activity was 00:10:15 ago (threshold: 00:10:00)
```

**System Active:**
```
System is active - last activity was 00:02:30 ago (threshold: 00:10:00)
```

**Background Service Suspension:**
```
System is idle, skipping outbox projection to allow SQL to idle
```

**Activity Recording:**
```
Recorded user activity: UserId={UserId}, TenantId={TenantId}
Recorded agent activity: AgentId={AgentId}, TenantId={TenantId}
```

### Observability

Monitor these metrics to verify the feature is working:

1. **SQL Connection Count**: Should drop to 0-1 during idle periods
2. **DTU Usage**: Should decrease significantly during idle periods
3. **Activity Table Size**: Should remain small (one row per active user/agent)
4. **Background Service Logs**: Should show "skipping" messages during idle

## Cost Savings

### Expected Impact

**Before Implementation:**
- SQL connections: Continuous (3-5 concurrent)
- Background tasks: Run every 10-30 seconds regardless of activity
- vCore usage: Baseline 5-10 DTU even when unused

**After Implementation:**
- SQL connections: Drop to zero during idle periods (10+ minutes no activity)
- Background tasks: Suspended during idle, resume on activity
- vCore usage: Can scale to zero during extended idle periods
- **Estimated savings**: 30-50% reduction in SQL costs for systems with sporadic usage

### Cost-Benefit Analysis

**Costs:**
- Azure Table Storage: ~$0.00005 per 10,000 transactions (negligible)
- Activity records: ~100 bytes per user/agent (minimal)

**Benefits:**
- Reduced SQL DTU usage: Potential $50-$200/month savings depending on tier
- Lower connection overhead: Reduced SQL connection management costs
- Improved sustainability: Database resources freed for other workloads

## Testing

### Unit Tests

The feature includes comprehensive unit tests in `ActivityMonitorTests.cs`:

- `IsSystemIdleAsync_WithNoActivity_ReturnsTrue`
- `IsSystemIdleAsync_WhenDisabled_ReturnsFalse`
- `RecordUserActivityAsync_DoesNotThrow`
- `RecordAgentActivityAsync_DoesNotThrow`
- `GetActiveUserCountAsync_ReturnsZeroOrMore`
- `GetActiveAgentCountAsync_ReturnsZeroOrMore`

### Manual Testing

To manually test the feature:

1. **Start the API** and log in as a user
2. **Monitor logs** for activity recording messages
3. **Wait 10+ minutes** without any user or agent activity
4. **Check logs** for idle detection and background service suspension messages
5. **Trigger activity** (login, agent heartbeat) and verify system resumes

### Integration Testing

To verify SQL idling:

1. Monitor SQL connection count using Azure Portal or SQL DMVs
2. Ensure connections drop to zero during extended idle periods
3. Verify connections resume immediately when activity detected

## Troubleshooting

### System Never Enters Idle State

**Symptom:** Logs show "System is active" even with no users/agents

**Possible Causes:**
1. Background services recording activity (check for agent heartbeats)
2. Long-running API requests keeping system active
3. Activity monitor configuration issue

**Solution:**
- Check `SystemActivity` table for recent entries
- Review agent logs to ensure agents are not constantly polling
- Verify `IdleTimeoutMinutes` configuration

### Background Services Not Suspending

**Symptom:** Background services continue running during idle

**Possible Causes:**
1. `EnableIdleSuspension` is set to false
2. Activity monitor service not registered
3. Background service not checking idle status

**Solution:**
- Verify `ActivityMonitor:EnableIdleSuspension` is true in configuration
- Check DI container registration for `IActivityMonitor`
- Review background service logs for idle check messages

### Activity Not Being Recorded

**Symptom:** No activity entries in `SystemActivity` table

**Possible Causes:**
1. Table Storage connection string not configured
2. Table creation failed
3. Activity monitor not injected into controllers

**Solution:**
- Verify `Storage:TablesEndpoint` or `Storage:ConnectionString` is configured
- Check startup logs for table creation errors
- Ensure controllers have `IActivityMonitor` in constructor

## Future Enhancements

### Planned Improvements

1. **Agent-Side Idle Detection**
   - Agents query idle status from API
   - Agents reduce ping frequency when idle
   - Agents suspend command polling when idle

2. **Adaptive Polling Intervals**
   - Background services increase polling intervals during low activity
   - Services return to normal intervals during high activity
   - Configurable scaling factors

3. **Activity-Based Auto-Scaling**
   - Trigger Azure SQL auto-scaling based on activity
   - Scale down during idle, scale up on activity
   - Integration with Azure Automation

4. **Dashboard Metrics**
   - Real-time idle status indicator in web UI
   - Activity timeline visualization
   - Cost savings estimates

## Security Considerations

### Data Privacy

- Activity records contain only IDs, no personal information
- Activity timestamps are aggregated, not logged in detail
- Table Storage is secured with connection strings or managed identity

### Fail-Safe Design

- System defaults to "active" on errors (safety first)
- Critical operations never suspended
- No data loss risk from idle suspension

### Access Control

- Activity monitoring uses existing authentication
- No new permissions required for users/agents
- Table Storage access controlled by connection string

## References

- [SQL Activity Audit](SQL_ACTIVITY_AUDIT.md) - Complete audit of SQL operations
- [Queue Commands](QUEUE_COMMANDS.md) - Queue-based command system
- [Azure Table Storage Quickstart](../AZURE-TABLE-QUICKSTART.md) - Table Storage setup guide
