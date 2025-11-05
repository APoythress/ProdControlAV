# SQL Database Elimination for Real-Time Operations

This document describes how ProdControlAV eliminates SQL database dependencies during normal agent and dashboard operations to minimize database costs and enable low/no-vCore periods.

## Overview

The system uses a **hybrid storage architecture** where:
- **Azure Table Storage** handles all real-time device status and monitoring data
- **Azure Queue Storage** handles agent command delivery
- **Azure SQL Database** serves only as the authoritative record for administrative operations, audit trails, and historical reporting

## Architecture Principles

### Real-Time Operations (No SQL)
During normal operations (agent monitoring, dashboard viewing), the system operates entirely without SQL database access:

```
Agent → Queue Storage ← API → Table Storage → Dashboard
   ↓                              ↑
Status Updates              Device Status
                            Device Metadata
```

### Administrative Operations (SQL Only)
SQL database is used only for infrequent administrative operations:

```
Admin → API → SQL Database → Outbox → Background Worker → Table Storage
         ↓
    Queue Storage (for commands)
```

## Endpoint Classification

### ✅ Real-Time Endpoints (No SQL - Production Ready)

#### Dashboard Reads
- **GET /api/devices/devices** - Device list with status
  - Source: `IDeviceStore` (Azure Table Storage)
  - Returns: Device metadata + real-time status
  - No SQL queries
  
- **GET /api/devices/actions** - Device action list
  - Source: `IDeviceActionStore` (Azure Table Storage)
  - Returns: Available actions per device
  - No SQL queries

- **GET /api/status?tenantId={id}** - Device status list
  - Source: `IDeviceStatusStore` (Azure Table Storage)
  - Returns: Current status for all devices
  - No SQL queries

#### Agent Operations
- **POST /api/agents/commands/receive** - Command polling (queue-based)
  - Source: `IAgentCommandQueueService` (Azure Queue Storage)
  - Returns: Next command from queue (if any)
  - No SQL queries during polling
  
- **GET /api/agents/devices** - Device list for agent
  - Source: `IDeviceStore` (Azure Table Storage)
  - Returns: Target devices for monitoring
  - No SQL queries
  
- **POST /api/agents/status** - Status upload
  - Target: `IDeviceStatusStore` + `IDeviceStore` (Azure Table Storage)
  - Writes status to Table Storage only
  - No SQL queries

- **POST /api/status** - Generic status update
  - Target: `IDeviceStatusStore` + `IDeviceStore` (Azure Table Storage)
  - Writes status to Table Storage only
  - No SQL queries

### ⚠️ Deprecated Endpoints (SQL Access - Avoid in Production)

- **POST /api/agents/commands/next** - Legacy SQL-based command polling
  - ❌ Marked as `[Obsolete]`
  - ❌ Queries SQL database directly
  - ⚠️ Logs deprecation warning on every call
  - ✅ Migrate to: `/api/agents/commands/receive`
  - Will be removed in future version

### 🔒 Administrative Endpoints (SQL Required - Low Frequency)

#### Device Management
- **POST /api/devices** - Create device
- **PUT /api/devices/{id}** - Update device
- **DELETE /api/devices/{id}** - Delete device
- **GET /api/devices/{id}** - Get single device details

All device management operations:
1. Write to SQL database (authoritative record)
2. Create Outbox entry
3. Background worker projects to Table Storage
4. Maintains data consistency

#### Command Management
- **POST /api/agents/commands/create** - Create command
  - Writes to SQL (audit record)
  - Enqueues to Azure Queue Storage (delivery)
  
- **POST /api/agents/commands/complete** - Report completion
  - Updates SQL (audit record)
  - Does not affect queue

#### Authentication & Heartbeat (Optimized for Zero SQL During Normal Operations)
- **POST /api/agents/auth** - Agent authentication
  - Reads from Table Storage (agent auth lookup)
  - No SQL queries during normal authentication
  
- **POST /api/agents/heartbeat** - Agent heartbeat
  - Records activity in Table Storage (via IActivityMonitor)
  - Only writes to SQL when agent metadata changes (hostname, IP, version)
  - No SQL writes for repeated heartbeats with unchanged metadata
  - This optimization prevents constant SQL writes and allows SQL to idle

## Data Flow Diagrams

### Real-Time Status Update Flow (No SQL)
```
Agent
  │
  ├─→ Ping devices (ICMP/TCP)
  │
  ├─→ POST /api/agents/status
  │     │
  │     ├─→ IDeviceStatusStore.UpsertAsync() → Azure Table Storage (DeviceStatus)
  │     └─→ IDeviceStore.UpsertStatusAsync() → Azure Table Storage (Devices)
  │
  └─→ Dashboard
        │
        └─→ GET /api/devices/devices
              │
              └─→ IDeviceStore.GetAllForTenantAsync() → Azure Table Storage
```

### Command Delivery Flow (No SQL During Polling)
```
Admin
  │
  └─→ POST /api/agents/commands/create
        │
        ├─→ SQL Database (audit record)
        └─→ Azure Queue Storage (delivery)
              │
              └─→ Agent polls: POST /api/agents/commands/receive
                    │
                    ├─→ IAgentCommandQueueService.ReceiveCommandAsync()
                    │
                    └─→ Executes command locally
                          │
                          └─→ POST /api/agents/commands/complete
                                │
                                └─→ SQL Database (completion record)
```

### Device Metadata Projection Flow (SQL → Table Storage)
```
Admin
  │
  └─→ POST /api/devices (create/update device)
        │
        ├─→ SQL Database (Device record)
        └─→ SQL Database (OutboxEntry)
              │
              └─→ DeviceProjectionHostedService (background worker)
                    │
                    ├─→ Reads OutboxEntries (batch: 50)
                    │
                    ├─→ IDeviceStore.UpsertAsync() → Azure Table Storage
                    │
                    └─→ Marks OutboxEntry as processed
```

## Cost Optimization Benefits

### Before (SQL-Dependent)
- Agent polls SQL every 10 seconds: ~8,640 queries/day/agent
- Dashboard refreshes SQL every 30 seconds: ~2,880 queries/day/user
- Device status updates SQL every 5 minutes: ~288 updates/day/device
- **Total for 10 agents + 5 users + 20 devices: ~100,000+ SQL queries/day**
- Requires minimum SQL tier: S1 (~$30/month) to handle load

### After (Table Storage)
- Agent polls Queue Storage: ~$0.0004/10,000 operations
- Dashboard reads Table Storage: ~$0.0004/10,000 operations
- Status writes Table Storage: ~$0.0050/10,000 operations
- **Total for 10 agents + 5 users + 20 devices: ~$0.50/month for storage operations**
- SQL can scale to zero during off-hours: ~$0/month when idle

### Cost Reduction
- **Storage operations: 99.5% reduction** (from $30/month to $0.50/month)
- **Off-hours scaling: 100% reduction** (SQL can be paused)
- **Total savings: ~$25-30/month minimum** (scales with usage)

## Migration Checklist

### For Existing Deployments

- [ ] **Verify Table Storage is populated**
  - Check Azure Portal: Table Storage → Devices table
  - Should contain all devices with metadata
  - Run backfill if empty (see `docs/IMPLEMENTATION_SUMMARY.md`)

- [ ] **Update Agent Configuration**
  - Ensure agents use `/api/agents/commands/receive` (not `/commands/next`)
  - Verify `CommandsEndpoint` in `appsettings.json`
  - Default behavior is correct if using latest agent version

- [ ] **Update Dashboard Configuration**
  - Verify dashboard uses `/api/devices/devices` endpoint
  - Should already be correct (no changes needed)

- [ ] **Monitor Deprecated Endpoint Usage**
  - Check Application Insights for deprecation warnings
  - Search logs for: `DEPRECATED endpoint called`
  - Identify and migrate any clients still using old endpoint

- [ ] **Validate SQL Query Reduction**
  - Monitor SQL DTU usage before/after
  - Check Application Insights for query counts
  - Verify 90%+ reduction in SELECT queries

### For New Deployments

- [ ] **Initial Setup**
  - Deploy SQL database (required for admin operations)
  - Create Azure Storage account with Tables + Queues
  - Run database migrations
  - Configure API connection strings

- [ ] **Backfill Table Storage**
  - Run initial device projection (see deployment docs)
  - Verify all devices appear in Table Storage
  - Verify all device actions appear in Table Storage

- [ ] **Deploy Agents**
  - Use latest agent version (includes queue-based polling)
  - Configure agent JWT authentication
  - Verify agents receive commands via queue

- [ ] **Verify Zero-SQL Operations**
  - Monitor agent operations (should not hit SQL)
  - Monitor dashboard loads (should not hit SQL)
  - Only admin operations should hit SQL

## Monitoring & Alerting

### Key Metrics to Track

#### Storage Operations (Azure Monitor)
- **Table Storage Reads**: Should be high (dashboard + agent queries)
- **Table Storage Writes**: Should match device status update frequency
- **Queue Storage Operations**: Should match agent polling frequency
- **SQL Database Queries**: Should be <100/hour during normal operations

#### Application Insights Queries

**SQL Query Count (Should Be Near Zero)**
```kusto
requests
| where timestamp > ago(1h)
| where cloud_RoleName == "ProdControlAV.API"
| where name contains "Devices" or name contains "Status" or name contains "Commands"
| extend hasSql = customDimensions.DependencyType == "SQL"
| summarize SqlQueries = countif(hasSql), TotalRequests = count() by bin(timestamp, 5m)
| extend SqlPercentage = (SqlQueries * 100.0) / TotalRequests
```

**Deprecated Endpoint Usage (Should Be Zero)**
```kusto
traces
| where timestamp > ago(24h)
| where message contains "DEPRECATED endpoint called"
| summarize Count = count() by bin(timestamp, 1h)
| order by timestamp desc
```

**Table Storage Operation Count**
```kusto
dependencies
| where timestamp > ago(1h)
| where type == "Azure table"
| summarize Operations = count(), AvgDuration = avg(duration) by name
| order by Operations desc
```

### Alerts to Configure

1. **SQL Query Spike**: Alert if SQL queries > 1000/hour during normal operations
2. **Deprecated Endpoint Usage**: Alert if `/commands/next` is called
3. **Table Storage Errors**: Alert if Table Storage error rate > 5%
4. **Outbox Processing Lag**: Alert if unprocessed outbox entries > 100

## Troubleshooting

### Issue: Agent not receiving commands

**Symptoms**: Agent polls but no commands arrive

**Check**:
1. Verify agent uses `/api/agents/commands/receive` (not `/commands/next`)
2. Check Azure Queue Storage for queue: `pcav-{tenantId}-{agentId}`
3. Verify command was enqueued after creation
4. Check Application Insights for queue errors

**Fix**:
- Update agent configuration to use new endpoint
- Verify queue connection string in API `appsettings.json`
- Check agent JWT token is valid

### Issue: Dashboard shows stale device status

**Symptoms**: Device status not updating in real-time

**Check**:
1. Verify dashboard uses `/api/devices/devices` endpoint
2. Check Table Storage for recent status updates
3. Verify agent is posting status successfully
4. Check `StatusController` logs for write errors

**Fix**:
- Verify agent authentication is working
- Check network connectivity between agent and API
- Verify Table Storage connection string in API

### Issue: High SQL database usage

**Symptoms**: SQL DTU usage remains high after migration

**Check**:
1. Search logs for deprecated endpoint usage
2. Check for custom queries directly accessing SQL
3. Verify outbox processing is not excessive
4. Review Application Insights for SQL query sources

**Fix**:
- Migrate any clients using deprecated endpoints
- Optimize outbox processing frequency (currently 10s)
- Consider increasing outbox batch size (currently 50)

## Future Enhancements

### Short Term
- [ ] Add `PingFrequencySeconds` to Table Storage schema
- [ ] Implement status reconciliation job (SQL ↔ Table Storage)
- [ ] Add custom metrics dashboard for storage operations

### Medium Term
- [ ] Remove deprecated `/commands/next` endpoint entirely
- [ ] Implement queue-based status updates (instead of direct API)
- [ ] Add Table Storage entity versioning for conflict resolution

### Long Term
- [ ] Evaluate Cosmos DB for global distribution
- [ ] Implement eventual consistency monitoring
- [ ] Add multi-region failover support

## References

- [QUEUE_COMMANDS.md](./QUEUE_COMMANDS.md) - Command queue architecture
- [IMPLEMENTATION_SUMMARY.md](./IMPLEMENTATION_SUMMARY.md) - Table Storage implementation details
- [azure-table-status-migration.md](../azure-table-status-migration.md) - Status migration guide
- [AZURE-TABLE-QUICKSTART.md](../AZURE-TABLE-QUICKSTART.md) - Quick start guide

## Acceptance Criteria Met

✅ **No background or real-time DB usage for normal operations**
- Agent polling uses Queue Storage
- Dashboard reads use Table Storage
- Status updates use Table Storage

✅ **SQL used for admin, audit trails, or historical reporting only**
- Device CRUD operations write to SQL (with Outbox projection)
- Command creation writes to SQL (audit) + Queue (delivery)
- Command completion writes to SQL (audit only)
- Authentication reads from SQL (infrequent)

✅ **All real-time endpoints migrated**
- Dashboard: Uses Table Storage exclusively
- Agent device list: Uses Table Storage
- Agent command polling: Uses Queue Storage
- Agent status updates: Uses Table Storage

✅ **Legacy endpoints deprecated**
- `/api/agents/commands/next` marked obsolete
- Logs warnings on every use
- Documentation updated with migration path
