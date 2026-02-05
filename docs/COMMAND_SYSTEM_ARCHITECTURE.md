# Command System Architecture - Technical Documentation

## Overview

The ProdControlAV command system enables users to create, queue, and execute commands on devices through a distributed architecture consisting of:
- **Web UI (Blazor WebAssembly)**: Command creation and management
- **API (ASP.NET Core)**: Command storage and queue management
- **Agent (Worker Service)**: Command polling and execution
- **Storage**: SQL Database + Azure Table Storage

## System Components

### 1. Web UI Layer (Blazor WebAssembly)
**File**: `src/ProdControlAV.WebApp/Pages/Commands.razor`

**Responsibilities**:
- Present command creation/edit form to users
- Validate command parameters (REST, Telnet, ATEM)
- Submit commands to API for storage

**Key Objects**:
- `CommandForm`: UI form model with all command fields
- `Command`: Domain model with database fields

### 2. API Layer (ASP.NET Core)
**Files**:
- `src/ProdControlAV.API/Controllers/CommandController.cs`
- `src/ProdControlAV.API/Controllers/AgentsController.cs`

**Responsibilities**:
- Store command definitions in SQL Database
- Queue commands for execution in Table Storage
- Poll endpoint for Agent to retrieve pending commands
- Record command execution history

**Key Objects**:
- `Command`: SQL entity with all command configuration
- `CommandQueueDto`: Queue message in Table Storage
- `CommandEnvelope`: Transport object sent to Agent

### 3. Storage Layer
**Files**:
- `src/ProdControlAV.Infrastructure/Services/TableCommandQueueStore.cs`
- `src/ProdControlAV.API/Data/AppDbContext.cs`

**Storage Types**:
- **SQL Database**: Command definitions, device configuration
- **Table Storage**: Command queue and execution history

**Key Tables**:
- SQL: `Commands` table with ATEM fields
- Table Storage: `CommandQueue` partitioned by TenantId
- Table Storage: `CommandHistory` for execution results

### 4. Agent Layer (Worker Service)
**File**: `src/ProdControlAV.Agent/Services/CommandService.cs`

**Responsibilities**:
- Poll API for pending commands (every 5-10 seconds)
- Parse command payload (JSON)
- Execute commands (REST, Telnet, ATEM)
- Report execution results back to API

**Key Methods**:
- `PollCommandsAsync()`: Fetch commands from API
- `ExecuteCommandAsync()`: Execute command based on type
- `ExecuteAtemCommandAsync()`: Execute ATEM-specific commands
- `RecordCommandHistoryAsync()`: Report execution results

## Complete Command Flow

```
┌─────────────────────────────────────────────────────────────────────────┐
│                          COMMAND LIFECYCLE                               │
└─────────────────────────────────────────────────────────────────────────┘

┌──────────────┐
│   USER       │
│ (Web Browser)│
└──────┬───────┘
       │ 1. Create/Edit Command
       │    - Fill form (name, device, type)
       │    - Select ATEM Function (if ATEM type)
       │    - Provide parameters (inputId, rate, etc.)
       ▼
┌─────────────────────────────────────────────────────────────────────┐
│  BLAZOR WEB UI                                                      │
│  Commands.razor                                                     │
│                                                                     │
│  ┌──────────────────────────────────────────────────────────┐     │
│  │ CommandForm (in-memory)                                  │     │
│  │  - CommandName, Description                              │     │
│  │  - DeviceId, CommandType                                 │     │
│  │  - AtemFunction, AtemInputId, AtemTransitionRate, etc.  │     │
│  └──────────────────────────────────────────────────────────┘     │
│                                                                     │
└────────────────────────────┬────────────────────────────────────────┘
                             │ 2. POST /api/commands
                             │    { commandName, deviceId, commandType,
                             │      atemFunction, atemInputId, ... }
                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│  API - Command Controller                                           │
│  CommandController.Create()                                         │
│                                                                     │
│  ┌──────────────────────────────────────────────────────────┐     │
│  │ 3. Validate Request                                      │     │
│  │    - Check device exists                                 │     │
│  │    - Validate ATEM-specific fields                       │     │
│  └──────────────────────────────────────────────────────────┘     │
│                                                                     │
│  ┌──────────────────────────────────────────────────────────┐     │
│  │ 4. Create Command Entity                                 │     │
│  │    Command {                                             │     │
│  │      CommandId, TenantId, DeviceId,                      │     │
│  │      CommandName, CommandType,                           │     │
│  │      AtemFunction, AtemInputId,                          │     │
│  │      AtemTransitionRate, AtemMacroId                     │     │
│  │    }                                                     │     │
│  └──────────────────────────────────────────────────────────┘     │
│                                                                     │
│  ┌──────────────────────────────────────────────────────────┐     │
│  │ 5. Save to SQL Database                                  │     │
│  │    INSERT INTO Commands (...)                            │     │
│  └──────────────────────────────────────────────────────────┘     │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
       │
       │ Command Saved ✓
       │
       │ USER CLICKS "TRIGGER" BUTTON
       │
       ▼ 6. POST /api/commands/{id}/trigger
┌─────────────────────────────────────────────────────────────────────┐
│  API - Command Controller                                           │
│  CommandController.Trigger()                                        │
│                                                                     │
│  ┌──────────────────────────────────────────────────────────┐     │
│  │ 7. Load Command from SQL                                 │     │
│  │    SELECT * FROM Commands WHERE CommandId = @id          │     │
│  └──────────────────────────────────────────────────────────┘     │
│                                                                     │
│  ┌──────────────────────────────────────────────────────────┐     │
│  │ 8. Check Device Online (if required)                     │     │
│  │    Query DeviceStatus from Table Storage                 │     │
│  └──────────────────────────────────────────────────────────┘     │
│                                                                     │
│  ┌──────────────────────────────────────────────────────────┐     │
│  │ 9. Create CommandQueueDto                                │     │
│  │    CommandQueueDto {                                     │     │
│  │      CommandId, TenantId, DeviceId,                      │     │
│  │      CommandName, CommandType,                           │     │
│  │      DeviceIp, DevicePort,                               │     │
│  │      AtemFunction, AtemInputId,                          │     │
│  │      AtemTransitionRate, AtemMacroId,                    │     │
│  │      Status = "Pending",                                 │     │
│  │      AttemptCount = 0                                    │     │
│  │    }                                                     │     │
│  └──────────────────────────────────────────────────────────┘     │
│                                                                     │
└────────────────────────────┬────────────────────────────────────────┘
                             │ 10. INSERT into Table Storage
                             │     Partition: TenantId
                             │     RowKey: CommandId
                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│  AZURE TABLE STORAGE                                                │
│  CommandQueue Table                                                 │
│                                                                     │
│  PartitionKey: {TenantId}                                           │
│  RowKey: {CommandId}                                                │
│  Status: Pending                                                    │
│  AttemptCount: 0                                                    │
│  [All ATEM fields stored here]                                      │
│                                                                     │
└────────────────────────────┬────────────────────────────────────────┘
                             │
                             │ ⏱️  POLLING LOOP (every 5-10 seconds)
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│  AGENT - Command Service                                            │
│  PollCommandsAsync()                                                │
│                                                                     │
│  ┌──────────────────────────────────────────────────────────┐     │
│  │ 11. POST /api/agents/commands/poll                       │     │
│  │     Authorization: Bearer {JWT Token}                    │     │
│  └──────────────────────────────────────────────────────────┘     │
│                                                                     │
└────────────────────────────┬────────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│  API - Agents Controller                                            │
│  AgentsController.PollCommandQueue()                                │
│                                                                     │
│  ┌──────────────────────────────────────────────────────────┐     │
│  │ 12. Query Table Storage                                  │     │
│  │     GetPendingForTenantAsync(tenantId)                   │     │
│  │     - Status = "Pending"                                 │     │
│  │     - OrderBy CreatedUtc                                 │     │
│  └──────────────────────────────────────────────────────────┘     │
│                                                                     │
│  ┌──────────────────────────────────────────────────────────┐     │
│  │ 13. Check Retry Limit                                    │     │
│  │     if (AttemptCount >= 3) {                             │     │
│  │       Mark as Failed                                     │     │
│  │       Return null                                        │     │
│  │     }                                                    │     │
│  └──────────────────────────────────────────────────────────┘     │
│                                                                     │
│  ┌──────────────────────────────────────────────────────────┐     │
│  │ 14. Mark as Processing                                   │     │
│  │     UPDATE Status = "Processing"                         │     │
│  │     AttemptCount = AttemptCount + 1                      │     │
│  └──────────────────────────────────────────────────────────┘     │
│                                                                     │
│  ┌──────────────────────────────────────────────────────────┐     │
│  │ 15. Create CommandEnvelope with Serialized Payload      │     │
│  │     CommandEnvelope {                                    │     │
│  │       CommandId, DeviceId,                               │     │
│  │       Verb = CommandType,                                │     │
│  │       Payload = JSON {                                   │     │
│  │         deviceId, commandName, commandType,              │     │
│  │         deviceIp, devicePort,                            │     │
│  │         atemFunction, atemInputId,                       │     │
│  │         atemTransitionRate, atemMacroId,                 │     │
│  │         ... all other fields ...                         │     │
│  │       }                                                  │     │
│  │     }                                                    │     │
│  └──────────────────────────────────────────────────────────┘     │
│                                                                     │
│  ┌──────────────────────────────────────────────────────────┐     │
│  │ 16. Return to Agent                                      │     │
│  │     200 OK { command: CommandEnvelope }                  │     │
│  └──────────────────────────────────────────────────────────┘     │
│                                                                     │
└────────────────────────────┬────────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│  AGENT - Command Service                                            │
│  ExecuteCommandAsync()                                              │
│                                                                     │
│  ┌──────────────────────────────────────────────────────────┐     │
│  │ 17. Parse Payload JSON                                   │     │
│  │     var payloadJson = JsonSerializer.Deserialize(        │     │
│  │       command.Payload                                    │     │
│  │     );                                                   │     │
│  │     var commandType = payloadJson["commandType"];        │     │
│  └──────────────────────────────────────────────────────────┘     │
│                                                                     │
│  ┌──────────────────────────────────────────────────────────┐     │
│  │ 18. Route Based on Command Type                          │     │
│  │     switch (commandType) {                               │     │
│  │       case "REST":                                       │     │
│  │         → ExecuteRestCommandAsync()                      │     │
│  │       case "Telnet":                                     │     │
│  │         → ExecuteTelnetCommandAsync()                    │     │
│  │       case "ATEM":                                       │     │
│  │         → ExecuteAtemCommandAsync()                      │     │
│  │     }                                                    │     │
│  └──────────────────────────────────────────────────────────┘     │
│                                                                     │
└────────────────────────────┬────────────────────────────────────────┘
                             │
                             ▼ ATEM Command Path
┌─────────────────────────────────────────────────────────────────────┐
│  AGENT - ExecuteAtemCommandAsync()                                  │
│                                                                     │
│  ┌──────────────────────────────────────────────────────────┐     │
│  │ 19. Extract ATEM Parameters from Payload                 │     │
│  │     deviceId = payload["deviceId"]                       │     │
│  │     deviceIp = payload["deviceIp"]                       │     │
│  │     atemFunction = payload["atemFunction"]               │     │
│  │     atemInputId = payload["atemInputId"]                 │     │
│  │     atemTransitionRate = payload["atemTransitionRate"]   │     │
│  └──────────────────────────────────────────────────────────┘     │
│                                                                     │
│  ┌──────────────────────────────────────────────────────────┐     │
│  │ 20. Route to ATEM Function                               │     │
│  │     switch (atemFunction.ToUpper()) {                    │     │
│  │       case "CUTTOPROGRAM":                               │     │
│  │         → AtemManager.CutToProgramAsync()                │     │
│  │       case "FADETOPROGRAM":                              │     │
│  │         → AtemManager.AutoToProgramAsync()               │     │
│  │       case "SETPREVIEW":                                 │     │
│  │         → Return "Not Implemented"                       │     │
│  │       case "RUNMACRO":                                   │     │
│  │         → Return "Not Implemented"                       │     │
│  │     }                                                    │     │
│  └──────────────────────────────────────────────────────────┘     │
│                                                                     │
│  ┌──────────────────────────────────────────────────────────┐     │
│  │ 21. Execute on ATEM Device                               │     │
│  │     - Connect to ATEM at deviceIp:devicePort             │     │
│  │     - Send LibAtem command                               │     │
│  │     - Return success/failure                             │     │
│  └──────────────────────────────────────────────────────────┘     │
│                                                                     │
└────────────────────────────┬────────────────────────────────────────┘
                             │ Result: Success/Failure
                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│  AGENT - RecordCommandHistoryAsync()                                │
│                                                                     │
│  ┌──────────────────────────────────────────────────────────┐     │
│  │ 22. POST /api/agents/commands/history                    │     │
│  │     Authorization: Bearer {JWT Token}                    │     │
│  │     {                                                    │     │
│  │       commandId, deviceId, commandName,                  │     │
│  │       success, errorMessage, response,                   │     │
│  │       httpStatusCode, executionTimeMs                    │     │
│  │     }                                                    │     │
│  └──────────────────────────────────────────────────────────┘     │
│                                                                     │
└────────────────────────────┬────────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────────┐
│  API - Agents Controller                                            │
│  AgentsController.RecordCommandHistory()                            │
│                                                                     │
│  ┌──────────────────────────────────────────────────────────┐     │
│  │ 23. Save to CommandHistory (Table Storage)               │     │
│  │     INSERT CommandHistoryDto                             │     │
│  │     - ExecutionId (new Guid)                             │     │
│  │     - Success, ErrorMessage, Response                    │     │
│  │     - ExecutionTimeMs                                    │     │
│  └──────────────────────────────────────────────────────────┘     │
│                                                                     │
│  ┌──────────────────────────────────────────────────────────┐     │
│  │ 24. Update CommandQueue Status                           │     │
│  │     if (Success) {                                       │     │
│  │       MarkAsSucceededAsync()                             │     │
│  │       → Status = "Succeeded"                             │     │
│  │     } else {                                             │     │
│  │       MarkAsFailedAsync()                                │     │
│  │       → Status = "Failed"                                │     │
│  │     }                                                    │     │
│  └──────────────────────────────────────────────────────────┘     │
│                                                                     │
│  ┌──────────────────────────────────────────────────────────┐     │
│  │ 25. Dequeue Command                                      │     │
│  │     DequeueAsync()                                       │     │
│  │     → DELETE from CommandQueue                           │     │
│  └──────────────────────────────────────────────────────────┘     │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
       │
       ▼
   ✅ COMMAND COMPLETE
   History recorded in Table Storage
```

## Key Design Decisions

### Why Use Payload JSON Instead of Passing CommandQueueDto Directly?

**Historical Context**: The original design used a `Payload` string field because:
1. **Flexibility**: JSON payload allows dynamic command structures without schema changes
2. **Decoupling**: Agent doesn't need to reference API DTOs
3. **Versioning**: Easier to add new fields without breaking older agents

**Problems with This Approach**:
1. **Data Loss**: If payload isn't serialized correctly, fields get dropped
2. **Type Safety**: No compile-time validation of payload structure
3. **Debugging**: Hard to trace where data is lost in serialization chain
4. **Redundancy**: Duplicating data between CommandQueueDto and Payload

**Better Alternative** (Recommended for Future):
- Pass `CommandQueueDto` directly to Agent
- Agent deserializes directly to strongly-typed object
- Eliminates payload serialization step
- Compile-time safety for all fields
- Easier to maintain and debug

### Current Fix Applied
The immediate fix ensures the payload is properly serialized in `AgentsController.PollCommandQueue()`:
- All CommandQueueDto fields (including ATEM fields) are serialized into JSON payload
- Agent parses this JSON to extract all parameters
- No data loss during transmission

## Retry Logic

### Max Retry Attempts: 3
**Location**: `AgentsController.PollCommandQueue()` line 510

**Logic**:
- `AttemptCount = 0`: Initial state, first poll will increment to 1
- `AttemptCount = 1`: Second poll will increment to 2
- `AttemptCount = 2`: Third poll will increment to 3
- `AttemptCount >= 3`: Command marked as Failed, not returned to Agent

**Why Commands Might Retry**:
1. Agent polls command but crashes before recording history
2. Network timeout during execution
3. ATEM device unreachable
4. Agent restart during execution

### Preventing Infinite Retries
The hardcoded limit of 3 attempts prevents infinite loops. If a command exceeds this:
1. Status set to "Failed"
2. Failure recorded in CommandHistory
3. Command removed from queue
4. Agent never receives it again

## Common Failure Scenarios

### 1. Missing ATEM Fields in Payload
**Symptom**: Command executes but ATEM operation fails with "Missing required property"
**Cause**: Payload not serialized with ATEM fields
**Fix**: Commit 29f968a - properly serialize all fields in CommandEnvelope creation

### 2. 401 Unauthorized During History Recording
**Symptom**: Command executes but history recording fails
**Cause**: JWT token expired, refresh failed
**Solution**: Check agent `appsettings.json` for valid ApiKey and TenantId

### 3. Commands Stuck in Processing
**Symptom**: Command shows "Processing" forever in Table Storage
**Cause**: Agent crashed after marking as processing but before execution
**Solution**: Automatic reset after 5 minutes of processing (see `GetStuckProcessingCommandsAsync`)

### 4. Exceeded Retry Limit
**Symptom**: Command marked as Failed with "exceeded max retry attempts"
**Cause**: Command failed 3 times (network, device offline, etc.)
**Solution**: Review CommandHistory for specific error messages

## Monitoring and Troubleshooting

### Key Log Messages

**API**:
```
[COMMANDS/POLL] Returning command {CommandId} for execution (attempt {AttemptCount} of 3)
[COMMANDS/HISTORY] Recorded execution for command {CommandId}, Success={Success}
[COMMANDS/POLL] Command {CommandId} exceeded max retry attempts
```

**Agent**:
```
Executing ATEM command: {AtemCommand} for device {DeviceId}
Command {CommandId} executed: Success={Success}, Message={Message}
Failed to record command history for {CommandId} (attempt {Attempt}/{MaxRetries})
```

### Debugging Steps

1. **Check Command Creation**:
   ```sql
   SELECT * FROM Commands WHERE CommandId = '{guid}'
   ```

2. **Check Queue Status**:
   - Azure Portal → Table Storage → CommandQueue
   - Look for Status, AttemptCount, QueuedUtc

3. **Check Execution History**:
   - Azure Portal → Table Storage → CommandHistory
   - Look for Success, ErrorMessage, ExecutionTimeMs

4. **Check Agent Logs**:
   ```bash
   journalctl -u prodcontrolav -f
   ```
   Look for payload content, ATEM command details

## Performance Considerations

### Polling Frequency
- Agent polls every 5-10 seconds
- No performance impact on SQL Database (uses Table Storage)
- Table Storage queries are efficient with PartitionKey (TenantId)

### Scalability
- Multiple agents can poll simultaneously (each for their tenant)
- Table Storage handles high throughput
- Commands processed FIFO per tenant

### Table Storage Cleanup
- Successful commands: Deleted after dequeue
- Failed commands: Deleted after max retries
- History retained for auditing (manual cleanup recommended)

## Future Improvements

### 1. Eliminate Payload JSON
**Change**: Pass CommandQueueDto directly to Agent
**Benefits**: Type safety, no serialization bugs, easier maintenance
**Impact**: Requires Agent to reference API DTOs (acceptable trade-off)

### 2. Command Batching
**Change**: Return multiple commands per poll
**Benefits**: Reduce HTTP requests, improve throughput
**Impact**: Need to handle batch execution in Agent

### 3. Real-time Updates
**Change**: Use SignalR for push notifications instead of polling
**Benefits**: Immediate execution, no polling overhead
**Impact**: More complex infrastructure, connection management

### 4. Retry Backoff Strategy
**Change**: Exponential backoff between retries (1min, 5min, 15min)
**Benefits**: Reduce unnecessary retries, give devices time to recover
**Impact**: Longer time to failure for genuinely broken commands

## Summary

The command system provides a robust, scalable way to execute device commands through a queue-based architecture. The key fix applied (commit 29f968a) ensures all ATEM command fields are properly serialized in the payload, resolving the data loss issue reported by the user.

For future maintainability, consider eliminating the JSON payload approach in favor of passing strongly-typed objects directly, which would prevent these types of serialization bugs entirely.
