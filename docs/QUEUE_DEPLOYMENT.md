# Deployment Guide: Queue-Based Agent Commands

This guide explains how to deploy the Azure Queue Storage-based command system to production.

## Prerequisites

1. **Azure Storage Account** with Queue Storage enabled
2. **Azure SQL Database** with the latest schema migration applied
3. **Agent version** supporting queue-based polling

## Deployment Steps

### 1. Apply Database Migration

The migration adds the `DueUtc` column to the `AgentCommands` table.

```bash
# Generate migration script (if not already generated)
cd src/ProdControlAV.API
dotnet ef migrations script --idempotent --output migrate.sql

# Review the script, then apply to your database
sqlcmd -S your-server.database.windows.net -d ProdControlAV -U your-user -i migrate.sql
```

Or use Azure Portal/Azure Data Studio to run the migration.

### 2. Create Azure Storage Queue

The queues are created automatically when the first command is created, but you can pre-create them:

Queue naming pattern: `pcav-{tenantId}-{agentId}` (lowercase, no hyphens in GUIDs)

Example:
- Main queue: `pcav-a1b2c3d4e5f6-7890abcdef12`
- Poison queue: `pcav-poison-a1b2c3d4e5f6-7890abcdef12`

### 3. Configure API

Update your API configuration with the Queue Storage connection string:

**appsettings.Production.json:**
```json
{
  "Storage": {
    "TablesEndpoint": "https://youraccount.table.core.windows.net",
    "QueueConnectionString": "DefaultEndpointsProtocol=https;AccountName=youraccount;AccountKey=yourkey;EndpointSuffix=core.windows.net",
    "MaxDequeueCount": 5
  }
}
```

**Or using environment variables:**
```bash
export QUEUE_CONNECTION_STRING="DefaultEndpointsProtocol=https;AccountName=youraccount;AccountKey=yourkey;EndpointSuffix=core.windows.net"
```

### 4. Deploy API

Deploy the updated API with queue support:

```bash
# Publish the API
cd src/ProdControlAV.API
dotnet publish -c Release -o ./publish

# Deploy to your hosting environment (Azure App Service, VM, etc.)
```

The API will now:
- Create commands in both SQL and Queue Storage
- Accept queue-based polling from agents
- Fall back to SQL polling for older agents

### 5. Update Agents

Deploy updated agent software to Raspberry Pi devices:

```bash
# On development machine
cd src/ProdControlAV.Agent
dotnet publish -c Release -r linux-arm64 --self-contained -o ./publish

# Copy to Raspberry Pi
scp -r ./publish/* pi@raspberrypi.local:/opt/prodcontrolav/agent/

# Restart agent service on Raspberry Pi
ssh pi@raspberrypi.local
sudo systemctl restart prodcontrolav-agent
```

Agents will automatically use the new queue-based polling endpoint.

### 6. Verify Deployment

**Check API logs:**
```bash
# Look for queue enqueue messages
grep "Enqueued command" api-logs.txt

# Verify agents are receiving from queue
grep "COMMANDS/RECEIVE" api-logs.txt
```

**Check Agent logs:**
```bash
# On Raspberry Pi
journalctl -u prodcontrolav-agent -f

# Look for queue-based polling
grep "Received.*commands" /var/log/prodcontrolav/agent.log
```

**Verify in Azure Portal:**
1. Navigate to Storage Account → Queues
2. Check for queues named `pcav-*`
3. Verify message count decreases as agents process commands

### 7. Monitor

Monitor these metrics:

**Azure Storage:**
- Queue message count (should trend toward 0)
- Queue ingress/egress operations
- Poison queue depth (should be 0 or very low)

**Azure SQL:**
- Reduced DTU usage (fewer queries)
- Command completion rate
- Failed command count

**Application Insights:**
- Command latency (enqueue to execution)
- Agent polling frequency
- Command success rate

## Rollback Plan

If issues occur, you can rollback without data loss:

1. **Revert API deployment** to previous version
2. **Agents continue working** - they fall back to SQL polling
3. **No database changes needed** - DueUtc column is nullable

The system is backward compatible, so you can rollback the API without affecting agents.

## Cost Savings

Expected cost reductions:

- **SQL Server:** 50-70% reduction in DTU usage from polling
- **Storage:** Queue operations are ~10x cheaper than SQL queries
- **Overall:** Estimated $50-200/month savings depending on agent count

## Troubleshooting

### Commands not delivered to agents

**Check:**
1. Queue connection string in API configuration
2. Agent has valid JWT token (check API logs)
3. Queue exists: `pcav-{tenantId}-{agentId}`
4. Agent is polling the correct endpoint

**Fix:**
```bash
# Restart API to reload configuration
systemctl restart prodcontrolav-api

# Restart agent
ssh pi@raspberrypi.local
sudo systemctl restart prodcontrolav-agent
```

### High poison queue count

This indicates commands are failing repeatedly.

**Investigate:**
1. Check SQL for failed commands: `SELECT * FROM AgentCommands WHERE Success = 0`
2. Review agent logs for execution errors
3. Verify device connectivity and credentials

**Fix:**
- Update device configuration
- Retry failed commands manually
- Clear poison queue if resolved

### SQL still showing high DTU usage

**Verify:**
1. Agents are using new queue endpoint (check API logs)
2. Old agents are updated to latest version
3. No custom scripts still polling SQL directly

## Gradual Migration

You can migrate agents gradually:

1. **Deploy API** with queue support (backward compatible)
2. **Update one agent** and monitor for 24 hours
3. **Update remaining agents** in batches
4. **Monitor SQL DTU** to verify reduction

This allows you to catch issues before full deployment.

## Security Considerations

- Queue connection string contains storage account key (keep secure)
- Use Azure Key Vault for production secrets
- Rotate storage account keys periodically
- Enable Azure Storage firewall for production

## Support

For issues or questions:
- Check logs: `/var/log/prodcontrolav/`
- Review documentation: `docs/QUEUE_COMMANDS.md`
- Contact: support@prodcontrolav.com
