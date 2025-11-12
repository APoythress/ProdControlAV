# Command System Documentation

## Overview
The Command System enables users to create, manage, and execute commands on devices through the ProdControlAV dashboard. Commands are stored in SQL Database for management, while execution and history tracking are handled exclusively through Azure Table Storage to minimize database costs during idle periods.

## Architecture

### Data Storage Strategy

#### SQL Database (Azure SQL DB)
- **Purpose**: Store command definitions and metadata
- **Usage**: Only accessed when users are actively logged in
- **Entities**:
  - `Command` - Command definitions with device association

#### Azure Table Storage
- **Purpose**: Store command execution queue and history
- **Usage**: Always active, supports background operations without SQL DB
- **Tables**:
  - `CommandQueue` - Pending commands waiting for execution
  - `CommandHistory` - Execution results and historical data

### Workflow

```
1. User creates command → SQL DB (Command table)
2. User triggers command → Queue to Table Storage (CommandQueue)
3. Agent polls queue → Retrieves from Table Storage
4. Agent executes command → REST API call to device
5. Agent records result → Table Storage (CommandHistory)
```

## Command Types

### REST API Commands
- Execute HTTP requests against device REST endpoints
- Support for GET, POST, PUT, DELETE, PATCH methods
- Configurable request body and headers (JSON format)
- Device IP and port are automatically resolved from device configuration

### Telnet Commands (Future)
- Execute telnet commands for legacy device support
- Text-based command strings

## API Endpoints

### Command Management (SQL DB)

#### GET /api/commands
List all commands for the current tenant
```json
Response: [
  {
    "commandId": "guid",
    "tenantId": "guid",
    "deviceId": "guid",
    "commandName": "Power On",
    "description": "Turn device power on",
    "commandType": "REST",
    "commandData": "/api/power/on",
    "httpMethod": "POST",
    "requestBody": "{\"state\":\"on\"}",
    "requestHeaders": "{\"Authorization\":\"Bearer token\"}",
    "createdUtc": "2025-11-12T00:00:00Z",
    "updatedUtc": null,
    "requireDeviceOnline": true
  }
]
```

#### GET /api/commands/device/{deviceId}
List commands for a specific device
```json
Response: [Command array]
```

#### GET /api/commands/{id}
Get a specific command by ID
```json
Response: {Command object}
```

#### POST /api/commands
Create a new command
```json
Request: {
  "deviceId": "guid",
  "commandName": "Power On",
  "description": "Turn device power on",
  "commandType": "REST",
  "commandData": "/api/power/on",
  "httpMethod": "POST",
  "requestBody": "{\"state\":\"on\"}",
  "requestHeaders": "{\"Authorization\":\"Bearer token\"}",
  "requireDeviceOnline": true
}

Response: {Created Command object}
```

#### PUT /api/commands/{id}
Update an existing command
```json
Request: {
  "commandName": "Power On Updated",
  "description": "Updated description",
  ...
}

Response: {Updated Command object}
```

#### DELETE /api/commands/{id}
Delete a command
```
Response: 204 No Content
```

### Command Execution (Table Storage)

#### POST /api/commands/{id}/trigger
Trigger command execution
```json
Response: {
  "success": true,
  "message": "Command queued for execution",
  "commandId": "guid",
  "deviceName": "Device Name"
}

Error Response (device offline): {
  "error": "device_offline",
  "message": "Command requires device to be online, but device is currently offline"
}
```

## Database Schema

### SQL DB - Command Table
```sql
CREATE TABLE Commands (
    CommandId uniqueidentifier PRIMARY KEY,
    TenantId uniqueidentifier NOT NULL,
    DeviceId uniqueidentifier NOT NULL,
    CommandName nvarchar(200) NOT NULL,
    Description nvarchar(1000) NULL,
    CommandType nvarchar(50) NOT NULL,
    CommandData nvarchar(2000) NULL,
    HttpMethod nvarchar(10) NULL,
    RequestBody nvarchar(max) NULL,
    RequestHeaders nvarchar(max) NULL,
    CreatedUtc datetimeoffset NOT NULL,
    UpdatedUtc datetimeoffset NULL,
    RequireDeviceOnline bit NOT NULL DEFAULT 1
);

CREATE INDEX IX_Commands_TenantId_DeviceId ON Commands(TenantId, DeviceId);
CREATE INDEX IX_Commands_TenantId_CommandName ON Commands(TenantId, CommandName);
```

### Table Storage - CommandQueue
```
Partition Key: TenantId (lowercase guid)
Row Key: {CommandId}_{QueuedUtc} (for chronological ordering)

Properties:
- CommandId (string)
- DeviceId (string)
- CommandName (string)
- CommandType (string)
- CommandData (string)
- HttpMethod (string)
- RequestBody (string)
- RequestHeaders (string)
- QueuedUtc (DateTimeOffset)
- QueuedByUserId (string)
- DeviceIp (string)
- DevicePort (int)
- Status (string) - "Pending", "Processing", "Completed"
```

### Table Storage - CommandHistory
```
Partition Key: TenantId (lowercase guid)
Row Key: ExecutionId (guid)

Properties:
- CommandId (string)
- DeviceId (string)
- CommandName (string)
- ExecutedUtc (DateTimeOffset)
- Success (bool)
- ErrorMessage (string)
- Response (string)
- HttpStatusCode (int)
- ExecutionTimeMs (double)
```

## UI Components

### Commands Page (/commands)
Full CRUD interface for managing commands:
- List all commands in a table
- Create new commands with modal form
- Edit existing commands
- Delete commands with confirmation
- Trigger command execution with status feedback

### Command Creation Form
Fields:
- **Command Name** (required) - Display name for the command
- **Device** (required) - Target device selection
- **Description** (optional) - Human-readable description
- **Command Type** (required) - REST or Telnet
- **HTTP Method** (REST only) - GET, POST, PUT, DELETE, PATCH
- **API Endpoint Path** (REST) / **Telnet Command** (Telnet) - Command data
- **Request Body** (REST, optional) - JSON payload
- **Request Headers** (REST, optional) - JSON headers
- **Require Device Online** (checkbox, default: true) - Prevent execution when device offline

### Trigger Command Modal
Displays:
- Command name and description
- Target device name
- Command type
- Device online status (with badge)
- Warning if device is offline and command requires online status

Actions:
- **Run** - Queue command for execution
- **Close** - Cancel without executing

## Security Considerations

### Tenant Isolation
- All SQL queries filtered by tenant ID via EF Core query filters
- Table Storage uses tenant ID as partition key for natural isolation
- API endpoints require tenant membership authentication

### Device IP Validation
- Only private IP addresses (RFC1918) are allowed
- Prevents SSRF attacks by rejecting public IPs
- IP validation performed before queueing command

### Authentication
- All command endpoints require `TenantMember` policy
- User must be logged in with valid tenant context
- Command execution records user ID for audit trail

## Cost Optimization

### Idle Backend Strategy
When no users are logged in:
- ✅ **Zero SQL DB interaction** - No reads or writes
- ✅ **Table Storage only** - All background operations use Table Storage
- ✅ **Agent operations** - Polling, execution, and history recording in Table Storage
- ✅ **Cost savings** - Reduces Azure SQL DTU usage by ~65%

### Active User Strategy
When users are logged in:
- ✅ **SQL DB for definitions** - Read command definitions for display
- ✅ **Table Storage for queue** - Write to queue when triggering
- ✅ **No execution in SQL** - All execution data stays in Table Storage

## Agent Integration (Future Work)

### Command Polling
Agents will poll the CommandQueue table for pending commands:
```csharp
// Poll for pending commands
var pendingCommands = await _commandQueueStore.GetPendingForTenantAsync(tenantId, ct);

foreach (var cmd in pendingCommands)
{
    // Mark as processing
    await _commandQueueStore.MarkAsProcessingAsync(tenantId, cmd.CommandId, ct);
    
    // Execute command
    var result = await ExecuteCommandAsync(cmd);
    
    // Record history
    await _commandHistoryStore.RecordExecutionAsync(new CommandHistoryDto(...), ct);
    
    // Remove from queue
    await _commandQueueStore.DequeueAsync(tenantId, cmd.CommandId, ct);
}
```

### REST Command Execution
For REST commands:
1. Build HTTP request using device IP, port, and command data
2. Add custom headers from command definition
3. Set request body for POST/PUT/PATCH requests
4. Execute with timeout (5 seconds default)
5. Record response and status code in history

### Error Handling
- Timeout errors recorded in history with null status code
- HTTP errors recorded with status code and response
- Network errors recorded with error message
- Failed commands remain in queue for retry logic (future)

## Testing

### Unit Tests (To Be Implemented)
- Command CRUD operations
- Command queue operations
- Command history recording
- Device online validation
- Tenant isolation

### Integration Tests (To Be Implemented)
- End-to-end command workflow
- Table Storage integration
- Agent command execution

### Manual Testing Checklist
- [ ] Create REST command via UI
- [ ] Create Telnet command via UI
- [ ] Update command
- [ ] Delete command
- [ ] Trigger command with device online
- [ ] Trigger command with device offline (should fail)
- [ ] Verify command queued in Table Storage
- [ ] Verify command execution by agent
- [ ] Verify history recorded in Table Storage
- [ ] Verify no SQL DB interaction when no users logged in

## Migration Guide

### Database Migration
Run the migration to create the Command table:
```bash
dotnet ef database update --project src/ProdControlAV.API
```

Or manually apply the migration script:
```sql
-- See: src/ProdControlAV.API/Migrations/20251112000000_AddCommandTable.cs
```

### Azure Table Storage Setup
The CommandQueue and CommandHistory tables are automatically created on first use by the TableServiceClient. No manual setup required.

### Configuration
Ensure Table Storage connection string is configured in appsettings.json:
```json
{
  "Storage": {
    "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net"
  }
}
```

## Troubleshooting

### Common Issues

#### Commands not appearing in UI
- Check user authentication and tenant context
- Verify SQL DB connection
- Check browser console for API errors

#### Trigger fails with "device_offline"
- Verify device is actually online via Dashboard
- Check device status in Table Storage (DeviceStatus table)
- Ensure `RequireDeviceOnline` setting matches intent

#### Command not executed by agent
- Check agent is running and authenticated
- Verify CommandQueue table has entries
- Check agent logs for polling activity
- Verify Table Storage connection string

#### REST command fails
- Check device IP is accessible from agent
- Verify API endpoint path is correct
- Check request body and headers are valid JSON
- Review command history for error details

## Future Enhancements

1. **Telnet Command Support** - Full implementation of telnet command execution
2. **Command Scheduling** - Schedule commands to run at specific times
3. **Command Retry Logic** - Auto-retry failed commands with exponential backoff
4. **Command Batching** - Execute multiple commands in sequence
5. **Command Templates** - Pre-defined templates for common operations
6. **Command History UI** - View execution history in dashboard
7. **Real-time Status** - WebSocket notifications for command completion
8. **Command Permissions** - Role-based access control for command execution
9. **Command Validation** - Pre-execution validation of command syntax
10. **Device Groups** - Execute commands across multiple devices simultaneously

## Support

For issues, feature requests, or questions:
1. Check this documentation
2. Review API endpoint responses for error details
3. Check application logs for detailed error information
4. Contact development team for assistance
