# Agent Table Storage Sync Guide

## Overview

To prevent the serverless SQL database from being queried frequently, all agent authentication records must be synced to Azure Table Storage. This guide explains how to verify and ensure all agents are synced.

## Why This Matters

The agent authentication flow has a fallback pattern:
1. First checks Azure Table Storage for the agent auth record (fast, no SQL hit)
2. If not found, falls back to SQL database (slow, keeps DB warm)
3. After finding in SQL, syncs the record to Table Storage for future lookups

If the Table Storage sync fails, agents will continue hitting the SQL database on every authentication attempt, preventing the serverless DB from pausing.

## Changes Made to Reduce DB Hits

### 1. JWT Token Expiry Extended
- **Before:** 30 minutes (48 auth requests per day)
- **After:** 8 hours (3 auth requests per day)
- **Impact:** 94% reduction in auth frequency

### 2. Table Storage Sync Retry Logic
- Added 3-attempt retry with exponential backoff (100ms, 200ms, 400ms)
- Improved error logging to detect persistent sync failures
- Prevents transient Table Storage issues from causing ongoing SQL hits

### 3. Agent API Call Frequencies
All agent API calls now use Table Storage exclusively except for the auth fallback:

| Endpoint | Frequency | Storage Used | SQL Hit? |
|----------|-----------|--------------|----------|
| `/api/agents/auth` | Every 8 hours | Table Storage (fallback to SQL) | Only if Table Storage sync fails |
| `/api/agents/devices` | Every 30 seconds | Table Storage only | No |
| `/api/agents/heartbeat` | Every 60 seconds | Table Storage (uses same auth validation) | Only if Table Storage sync fails |
| `/api/agents/status` | On status changes | Table Storage only | No |
| `/api/agents/commands/poll` | Every 10 seconds | Table Storage only | No |
| `/api/agents/commands/history` | On command completion | Table Storage only | No |

## Verifying Agent Sync Status

### Check Azure Table Storage

1. Navigate to your Azure Storage Account in Azure Portal
2. Go to **Tables** → **AgentKeyHashIndex**
3. Verify entries exist for all active agents

Each agent should have:
- **PartitionKey:** First 4 characters of agent key hash
- **RowKey:** Full agent key hash
- **AgentId:** GUID of the agent
- **TenantId:** GUID of the tenant

### Check Application Logs

Look for these log messages to identify sync issues:

**Successful sync:**
```
[Information] Successfully synced agent to table store: AgentId={AgentId} (attempt 1)
```

**Failed sync (warning):**
```
[Warning] Failed to sync agent {AgentId} to Table Storage after retries. Agent will continue to hit SQL on next auth.
```

**Agent using SQL fallback:**
```
[Information] Agent not found in table store for hash {Hash}, checking database
```

If you see frequent "checking database" messages, it means agents are not properly synced to Table Storage.

## Manually Sync Agents to Table Storage

If you need to manually ensure all agents are synced, you can run this SQL query to identify which agents need syncing, then trigger an authentication for each:

```sql
-- Find all active agents
SELECT 
    Id,
    TenantId,
    Name,
    AgentKeyHash,
    LastSeenUtc,
    Version
FROM Agents
WHERE IsActive = 1
ORDER BY LastSeenUtc DESC;
```

Then for each agent:
1. Trigger an authentication by restarting the agent service
2. The first auth will sync the agent to Table Storage
3. Verify sync success in application logs

## Monitoring Recommendations

### SQL Database Query Monitoring

Monitor your Azure SQL Database for:
- **Connection count:** Should drop to 0 after 15 minutes of no user activity
- **DTU/CPU usage:** Should show idle periods
- **Query patterns:** Look for frequent `SELECT * FROM Agents WHERE AgentKeyHash = ...`

### Table Storage Monitoring

Monitor Azure Table Storage for:
- **Transaction count:** Should be high (all agent operations)
- **Latency:** Should be low (<50ms for 99th percentile)
- **Error rate:** Should be <0.1%

### Application Insights

Set up alerts for:
- Log pattern: `"Failed to sync agent to table store after"`
- Frequency: More than 5 occurrences in 15 minutes
- Action: Investigate Table Storage connectivity issues

## Troubleshooting

### Agents Keep Hitting SQL Database

**Symptom:** Logs show frequent "checking database" messages

**Diagnosis:**
1. Check if Table Storage is accessible from the API
2. Verify connection string is correct in `appsettings.json`
3. Check if Table Storage has connectivity issues

**Resolution:**
1. Verify `Storage:TablesEndpoint` configuration
2. Test Table Storage connectivity:
   ```bash
   az storage table list --connection-string "YOUR_CONNECTION_STRING"
   ```
3. Check Azure Table Storage metrics for errors
4. Restart API to retry agent syncs

### Table Storage Sync Failures

**Symptom:** Logs show "Failed to sync agent to table store after 3 attempts"

**Diagnosis:**
1. Table Storage connectivity issues
2. Table Storage throttling (too many requests)
3. Authentication issues with storage account

**Resolution:**
1. Check Azure Table Storage service health
2. Verify storage account access keys are valid
3. Consider scaling up Table Storage tier if throttling occurs
4. Review Table Storage firewall rules

### Database Still Not Pausing

**Symptom:** SQL Database never enters pause state despite no user activity

**Diagnosis:**
1. Check for agents still hitting SQL (see "Agents Keep Hitting SQL Database" above)
2. Check for background services keeping DB warm
3. Verify idle timeout configuration

**Resolution:**
1. Ensure all agents are synced to Table Storage
2. Verify `ActivityMonitor:IdleTimeoutMinutes` is set correctly (default: 10 minutes)
3. Check `DeviceProjectionHostedService` logs - it should skip DB queries when idle:
   ```
   [Debug] System is idle, skipping outbox projection to allow SQL to idle
   ```

## Expected Results

After implementing these changes, you should see:

1. **JWT Authentication:**
   - Reduced from 48 times/day to 3 times/day per agent
   - 94% reduction in auth frequency

2. **SQL Database Connections:**
   - Agent authentication: Only during initial sync (one-time per agent)
   - Heartbeat: Only when agent metadata changes (rare)
   - Device projection: Only when system is not idle (user activity present)

3. **Serverless DB Behavior:**
   - Should pause after 15 minutes of no user activity
   - Should remain paused when only agents are active
   - Should resume only when users access the web app

4. **Table Storage Usage:**
   - All agent operations use Table Storage
   - Fast response times (<50ms)
   - High transaction count (expected and cost-effective)

## Cost Analysis

### Before Optimization
- SQL Database: Always on due to agent polling
- Estimated cost: $40-80/month (never pauses)
- Table Storage: Minimal usage

### After Optimization
- SQL Database: Pauses when no users active
- Estimated cost: $5-20/month (auto-pause enabled)
- Table Storage: All agent operations
- Estimated cost: $2-5/month
- **Total savings:** ~50-75% reduction

## References

- [Azure SQL Database Auto-Pause Documentation](https://learn.microsoft.com/en-us/azure/azure-sql/database/serverless-tier-overview)
- [Azure Table Storage Best Practices](https://learn.microsoft.com/en-us/azure/storage/tables/table-storage-design)
- Repository Documentation: `/docs/azure-table-device-projection.md`
