# Deployment Checklist: Reduce DB Connections

This checklist helps ensure the changes to reduce database connections are deployed successfully.

## Pre-Deployment

- [ ] Review changes in this PR
- [ ] Ensure you have access to:
  - Azure Portal (Storage Account and SQL Database)
  - Application Insights or logging system
  - Agent deployment infrastructure

## Deployment Steps

### 1. Update API Configuration

The JWT token expiry has been increased from 30 minutes to 8 hours. Ensure this is acceptable for your security requirements.

If you need a different expiry time, update `src/ProdControlAV.API/appsettings.json`:
```json
"Jwt": {
  "ExpiryMinutes": 480  // Change to desired value in minutes
}
```

### 2. Deploy API Changes

Deploy the updated API to your production environment:

```bash
# Build and publish the API
cd src/ProdControlAV.API
dotnet publish -c Release -o ./publish

# Deploy to your hosting environment (Azure App Service, etc.)
```

### 3. Verify Table Storage Connectivity

After deployment, test the health check endpoints:

```bash
# Basic health check
curl https://your-api-domain.com/api/health

# Table Storage connectivity
curl https://your-api-domain.com/api/health/storage

# Agent sync status (requires admin authentication)
curl -H "Authorization: Bearer YOUR_ADMIN_TOKEN" \
  https://your-api-domain.com/api/health/agent-sync
```

Expected response for healthy Table Storage:
```json
{
  "status": "healthy",
  "timestamp": "2025-...",
  "checks": {
    "agentAuthStore": {
      "healthy": true,
      "responseTimeMs": 45.2,
      "message": "AgentAuthStore responding"
    },
    ...
  }
}
```

### 4. Restart All Agents

Restart all agent instances to trigger JWT token refresh and ensure they sync to Table Storage:

```bash
# For systemd-based agents on Raspberry Pi
sudo systemctl restart prodcontrol-agent

# Or if running as a Docker container
docker restart prodcontrol-agent
```

### 5. Monitor Initial Agent Authentication

Watch the API logs for successful agent syncs:

**Look for:**
```
[Information] Successfully synced agent to table store: AgentId={AgentId} (attempt 1)
```

**Watch out for:**
```
[Warning] Failed to sync agent {AgentId} to Table Storage after retries. Agent will continue to hit SQL on next auth.
```

If you see sync failures, check:
- Table Storage connection string is correct
- Tables `Agents` and `AgentKeyHashIndex` exist
- Network connectivity to Azure Table Storage

### 6. Verify SQL Database Pausing

After 15-20 minutes of no user activity (only agents running):

1. Check Azure SQL Database metrics:
   - Navigate to Azure Portal → SQL Database → Metrics
   - Look for "CPU percentage" - should drop to 0%
   - Look for "Data space used" status - should show "Paused" if using serverless

2. Check database connection count:
   ```sql
   SELECT 
       COUNT(*) as ActiveConnections,
       MAX(last_request_end_time) as LastRequestTime
   FROM sys.dm_exec_sessions
   WHERE is_user_process = 1;
   ```

Expected: Connection count should drop to 0 or near-0 during idle periods.

### 7. Monitor for 24 Hours

Monitor the system for 24 hours to ensure:

**Metrics to watch:**
- SQL Database connections during agent-only periods (should be 0)
- SQL Database auto-pause events (should occur after idle period)
- Agent authentication frequency (should be ~3 per day per agent)
- Table Storage transaction count (should be high - this is expected)
- Application errors in logs (should be none)

**Set up alerts for:**
```
Log pattern: "Failed to sync agent to table store after"
Frequency: More than 5 occurrences in 15 minutes
Action: Check Table Storage connectivity
```

## Post-Deployment Verification

### Verify Agent Authentication Frequency

Check agent logs for auth requests. Should see approximately:
- 3 JWT auth requests per day (every 8 hours)
- 1 heartbeat per minute (uses existing token or API key validation)

### Verify Table Storage Usage

In Azure Portal → Storage Account → Metrics:
- Check transaction count - should be high (this is normal and cost-effective)
- Check latency - should be low (<100ms)
- Check error rate - should be <0.1%

### Verify Cost Reduction

After 1 week, compare SQL Database costs:
- **Before:** Database always active (~$40-80/month for serverless that never pauses)
- **After:** Database auto-pauses (~$5-20/month with auto-pause enabled)
- **Expected savings:** 50-75%

## Rollback Plan

If issues occur, you can rollback:

### Quick Rollback (reduce JWT expiry only)

Edit `appsettings.json`:
```json
"Jwt": {
  "ExpiryMinutes": 30  // Revert to original value
}
```

Redeploy API only (no agent changes needed).

### Full Rollback

```bash
# Revert to previous commit
git revert HEAD~2..HEAD

# Redeploy API
cd src/ProdControlAV.API
dotnet publish -c Release -o ./publish
# Deploy...
```

## Success Criteria

✅ All health checks return "healthy"
✅ No "Failed to sync agent" warnings in logs
✅ SQL Database pauses after 15 minutes of no user activity
✅ Agent auth frequency reduced to ~3 per day
✅ All agent functionality working normally
✅ Cost reduction visible in Azure billing

## Support

If you encounter issues:

1. Check the troubleshooting guide: `/docs/table-storage-agent-sync.md`
2. Review health check endpoints: `/api/health/storage`
3. Check application logs for sync failures
4. Verify Table Storage connectivity

## Notes

- The changes are backward compatible - agents will work with both old and new API
- JWT tokens issued before deployment will still be valid until they expire
- Table Storage costs are minimal (~$2-5/month) compared to SQL savings
- The first agent authentication after deployment will sync to Table Storage
- Subsequent authentications will use Table Storage exclusively (no SQL hit)
