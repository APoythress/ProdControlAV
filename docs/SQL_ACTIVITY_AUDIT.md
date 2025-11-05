# SQL Activity Audit: Recurring and Long-Running Queries

This document catalogs all recurring or long-running SQL queries in the ProdControlAV system, identifying opportunities for idle periods where the database can truly rest.

## Executive Summary

**Current State**: The database is under continuous load from:
- Agent heartbeats and status updates (every 10-60 seconds)
- Background projection service (every 10 seconds)
- Table retention cleanup (every 24 hours)
- Command polling (every 10 seconds via agents)

**Goal**: Enable periods where SQL database can idle when no active users/agents are present.

## 1. Agent Service (ProdControlAV.Agent)

### 1.1 Device Status Publishing
**Location**: `src/ProdControlAV.Agent/Services/StatusPublisher.cs`

**SQL Operations**:
- `POST /api/agents/status` - Inserts device status into Azure Table Storage (DeviceStatus table)
- Triggered on device state changes and periodic heartbeats

**Frequency**: 
- Heartbeat: Every 30 seconds (configurable via `HeartbeatSeconds`)
- State changes: On-demand when device status transitions

**vCore Impact**: 
- Low per operation (~5-10 DTU per batch)
- Cumulative: Prevents SQL idle when agents are running

**Idle Strategy**:
- ✅ **Can be suspended** - Device status is not critical for idle periods
- Uses Azure Table Storage, not SQL directly
- Agent can buffer status updates and resume when system becomes active

### 1.2 Command Polling
**Location**: `src/ProdControlAV.Agent/Services/CommandService.cs` → `AgentService.cs` (RunCommandPollLoop)

**SQL Operations**:
- `POST /api/agents/commands/receive` - Polls Azure Queue Storage for commands
- `POST /api/agents/commands/complete` - Updates command completion in SQL

**Frequency**: Every 10 seconds (configurable via `CommandPollIntervalSeconds`)

**vCore Impact**:
- Low per poll (~2-5 DTU)
- Queue polling hits Azure Storage, not SQL directly
- SQL is only touched for command completion records

**Idle Strategy**:
- ✅ **Can be throttled** - Queue polling can be paused during idle
- Command completion SQL writes only occur when commands are executed
- Agent can suspend polling when no users are active

### 1.3 Device Ping/Probe Operations
**Location**: `src/ProdControlAV.Agent/Services/AgentService.cs` (RunPollLoop)

**SQL Operations**:
- None directly - Uses local state and publishes to Table Storage
- Indirect SQL via status updates (see 1.1)

**Frequency**: 
- Base interval: Every 5 seconds (configurable via `IntervalMs`)
- Per-device frequency: Respects `PingFrequencySeconds` property

**vCore Impact**: None directly (network operations only)

**Idle Strategy**:
- ✅ **Can be suspended** - Pinging is monitoring activity, not critical during idle
- Agent should stop pinging when system is idle

## 2. API Background Services

### 2.1 Device Projection Hosted Service
**Location**: `src/ProdControlAV.API/Services/DeviceProjectionHostedService.cs`

**SQL Operations**:
- `SELECT * FROM OutboxEntries WHERE ProcessedUtc IS NULL` - Reads unprocessed outbox entries
- `UPDATE OutboxEntries SET ProcessedUtc = @now` - Marks entries as processed
- Batch size: 50 entries per cycle

**Frequency**: Every 10 seconds (constant: `PollingIntervalSeconds = 10`)

**vCore Impact**: 
- Medium-High (~10-20 DTU per poll when entries exist)
- **PRIMARY HOTSPOT** - Continuous polling prevents SQL idle

**Idle Strategy**:
- ✅ **Can be suspended** - Outbox projection is eventual consistency
- Should check activity monitor before each poll cycle
- Safe to delay projection during idle periods

### 2.2 Table Retention Enforcement Service
**Location**: `src/ProdControlAV.API/Services/TableRetentionEnforcementService.cs`

**SQL Operations**:
- None directly - Operates on Azure Table Storage only
- Queries and deletes entries from DeviceStatus table older than 8 days

**Frequency**: Every 24 hours (constant: `ScanIntervalHours = 24`)

**vCore Impact**: None (uses Azure Table Storage)

**Idle Strategy**:
- ✅ **Already optimal** - No SQL impact
- Runs infrequently (daily)
- Can safely run during any period

## 3. API Controllers (On-Demand SQL Queries)

### 3.1 AgentsController
**Location**: `src/ProdControlAV.API/Controllers/AgentsController.cs`

**SQL Operations**:
- `POST /api/agents/auth` - Validates agent key, generates JWT
  - `SELECT * FROM Agents WHERE ApiKey = @key AND TenantId = @tid`
- `POST /api/agents/status` - Writes to Azure Table Storage (no SQL)
- `POST /api/agents/commands/complete` - Updates command completion
  - `UPDATE AgentCommands SET CompletedUtc = @now, Success = @success WHERE CommandId = @id`

**Frequency**: On-demand (triggered by agent requests)

**vCore Impact**: Low per request (~2-5 DTU)

**Idle Strategy**:
- ✅ **Event-driven** - Only runs when agents are active
- Auth requests indicate agent activity (should update activity monitor)
- Command completion is transactional, cannot be delayed

### 3.2 DevicesController
**Location**: `src/ProdControlAV.API/Controllers/DevicesController.cs`

**SQL Operations**:
- `GET /api/devices` - Lists devices for tenant
  - `SELECT * FROM Devices WHERE TenantId = @tid`
- `POST /api/devices` - Creates new device
  - `INSERT INTO Devices (...) VALUES (...)`
- `PUT /api/devices/{id}` - Updates device
  - `UPDATE Devices SET ... WHERE Id = @id AND TenantId = @tid`
- `DELETE /api/devices/{id}` - Deletes device
  - `DELETE FROM Devices WHERE Id = @id AND TenantId = @tid`

**Frequency**: On-demand (user-initiated via web UI)

**vCore Impact**: Low per request (~2-5 DTU)

**Idle Strategy**:
- ✅ **Event-driven** - Only runs when users are active
- User requests indicate system activity (should update activity monitor)
- Cannot be suspended (user-facing operations)

### 3.3 CommandController
**Location**: `src/ProdControlAV.API/Controllers/CommandController.cs`

**SQL Operations**:
- `POST /api/agents/commands/create` - Creates command in SQL and enqueues
  - `INSERT INTO AgentCommands (...) VALUES (...)`
- `POST /api/agents/commands/receive` - Receives from queue (no SQL)
- `GET /api/agents/commands/history` - Queries command history
  - `SELECT * FROM AgentCommands WHERE TenantId = @tid ORDER BY CreatedUtc DESC`

**Frequency**: On-demand (user-initiated commands, agent polling)

**vCore Impact**: Low per request (~2-5 DTU)

**Idle Strategy**:
- ✅ **Event-driven** - Command creation indicates user activity
- Queue polling by agents indicates agent activity
- History queries are user-initiated

### 3.4 AuthController
**Location**: `src/ProdControlAV.API/Controllers/AuthController.cs`

**SQL Operations**:
- `POST /api/auth/signin` - Authenticates user
  - `SELECT * FROM Users WHERE Username = @user`
  - `SELECT * FROM UserTenants WHERE UserId = @uid`
- `POST /api/auth/register` - Creates new user
  - `INSERT INTO Users (...) VALUES (...)`
  - `INSERT INTO UserTenants (...) VALUES (...)`

**Frequency**: On-demand (user-initiated)

**vCore Impact**: Low per request (~5-10 DTU)

**Idle Strategy**:
- ✅ **Event-driven** - User login is key activity indicator
- **Should update activity monitor** when user signs in
- Cannot be suspended (authentication required)

## 4. Summary: Opportunities for Idle Optimization

### High-Priority Targets (Prevent SQL Idle)

1. **DeviceProjectionHostedService** (Every 10 seconds)
   - **Impact**: HIGH - Continuous polling is primary SQL hotspot
   - **Solution**: Add idle check before each poll cycle
   - **Savings**: 100% of polling load during idle periods

2. **Agent Status Publishing** (Every 30 seconds per agent)
   - **Impact**: MEDIUM - Uses Table Storage, but agents generate API traffic
   - **Solution**: Agent should check idle status before heartbeat
   - **Savings**: Eliminates API traffic during idle periods

3. **Agent Command Polling** (Every 10 seconds per agent)
   - **Impact**: LOW - Uses Queue Storage, minimal SQL
   - **Solution**: Agent should suspend polling during idle
   - **Savings**: Reduces API traffic during idle periods

### Event-Driven (Already Optimal)

- All API controller endpoints are event-driven
- They only execute SQL when users or agents make requests
- **Strategy**: Use these requests to update activity monitor timestamps

### Configuration Recommendations

**appsettings.json additions**:
```json
{
  "ActivityMonitor": {
    "IdleTimeoutMinutes": 10,
    "CheckIntervalSeconds": 30,
    "EnableIdleSuspension": true,
    "TableStorageConnectionString": "...",
    "CriticalOperations": ["Alarms", "Alerts"]
  }
}
```

## 5. Implementation Phases

### Phase 1: Activity Tracking
- Create `IActivityMonitor` interface
- Implement `DistributedActivityMonitor` using Azure Table Storage
- Track user login events (AuthController)
- Track agent authentication events (AgentsController)
- Store activity timestamps with TTL

### Phase 2: Background Service Integration
- Modify `DeviceProjectionHostedService` to check idle status
- Modify `AgentService` to check idle status (all three loops)
- Add configuration options for idle thresholds
- Log idle state transitions

### Phase 3: Testing & Validation
- Monitor SQL connection count during idle periods
- Verify system resumes promptly on activity
- Test critical operations bypass idle suspension
- Load test to ensure no performance degradation

## 6. Expected Outcomes

**Before Implementation**:
- SQL connections: Continuous (never zero)
- Background tasks: Run every 10-30 seconds regardless of activity
- vCore usage: Baseline 5-10 DTU even when system is unused

**After Implementation**:
- SQL connections: Drop to zero during idle periods (10+ minutes no activity)
- Background tasks: Suspended during idle, resume on activity
- vCore usage: Can scale to zero during extended idle periods
- Cost savings: Potential 30-50% reduction in SQL costs for low-usage periods
