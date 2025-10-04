# Azure Queue Storage Command System

This document describes the Azure Queue Storage-based command delivery system for ProdControlAV agents.

## Overview

The system uses **Azure Queue Storage** for efficient command delivery to agents while maintaining **Azure SQL Database** as the authoritative record for audit and administrative purposes. This design pattern reduces SQL Server load and operational costs.

## Architecture

### Components

1. **SQL Database** (Azure SQL) - Authoritative record
   - Stores all command history with timestamps
   - Used by dashboard for command history and reporting
   - Contains: `CreatedUtc`, `TakenUtc`, `CompletedUtc`, `Success`, `Message`

2. **Queue Storage** (Azure Storage Queues) - Command delivery
   - Delivers commands to agents efficiently
   - Per-tenant, per-agent queues: `pcav-{tenantId}-{agentId}`
   - Supports scheduled execution via visibility timeouts
   - Automatic poison queue for failed commands

3. **Agent** (Raspberry Pi)
   - Polls queue storage for commands (not SQL)
   - Executes commands locally
   - Reports completion back to SQL
   - Acknowledges successful receipt by deleting from queue

## Command Flow

### Creating a Command

```http
POST /api/agents/commands/create
Authorization: Bearer {user-token}
Content-Type: application/json

{
  "agentId": "guid",
  "deviceId": "guid",
  "verb": "PING",
  "payload": null,
  "dueUtc": "2025-01-01T12:00:00Z"  // optional
}
```

**Process:**
1. Validates agent and device belong to tenant
2. Creates record in SQL `AgentCommands` table
3. Enqueues message to Azure Queue Storage
4. Sets visibility delay based on `dueUtc` (if provided)

### Agent Polling Loop

Agents poll the queue every few seconds (configurable):

```http
POST /api/agents/commands/receive
Authorization: Bearer {agent-jwt-token}
```

**Process:**
1. Receives message from queue with 60s visibility timeout
2. Returns command with `messageId` and `popReceipt`
3. Returns `null` if no messages available
4. Checks dequeue count for poison message handling

### Command Execution

**On Success:**
```http
POST /api/agents/commands/acknowledge
{
  "messageId": "...",
  "popReceipt": "..."
}

POST /api/agents/commands/complete
{
  "commandId": "guid",
  "success": true,
  "message": "Command executed successfully",
  "durationMs": 123
}
```

**On Failure:**
- Message becomes visible again after 60s
- Dequeue count increments
- After 5 failed attempts:
  - Moved to poison queue: `pcav-poison-{tenantId}-{agentId}`
  - Marked as failed in SQL
  - Deleted from main queue

## Configuration

### API (appsettings.json)

```json
{
  "Storage": {
    "QueueConnectionString": "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net",
    "MaxDequeueCount": 5
  }
}
```

### Agent (appsettings.json)

```json
{
  "Api": {
    "BaseUrl": "https://api.prodcontrolav.com",
    "CommandsEndpoint": "/api/agents/commands/next",
    "CommandCompleteEndpoint": "/api/agents/commands/complete",
    "CommandPollIntervalSeconds": 10
  }
}
```

**Note:** The agent will automatically use `/commands/receive` endpoint for queue-based polling.

## Environment Variables

### API

- `QUEUE_CONNECTION_STRING` - Azure Storage Queue connection string
- `DB_CONNECTION_STRING` - Azure SQL Database connection string

### Agent

- `API_BASE_URL` - API base URL
- `AGENT_API_KEY` - Agent API key for authentication
- `AGENT_TENANT_ID` - Tenant ID for the agent

## Benefits

### Cost Optimization

- **Reduced SQL queries**: Agents no longer poll SQL database
- **Lower DTU usage**: Queue Storage handles high-frequency polling
- **Cheaper storage**: Queue operations cost less than SQL transactions

### Performance

- **Faster polling**: Queue Storage has lower latency than SQL
- **Scalability**: Queue Storage scales automatically
- **No database locks**: Eliminates contention on SQL

### Reliability

- **Poison queue handling**: Failed commands don't block the queue
- **Automatic retries**: Messages become visible again on failure
- **Audit trail**: SQL maintains complete command history

## Scheduled Commands

Commands can be scheduled for future execution using the `dueUtc` field:

```json
{
  "agentId": "...",
  "deviceId": "...",
  "verb": "REBOOT",
  "dueUtc": "2025-01-01T03:00:00Z"
}
```

The message will be invisible in the queue until the specified time (up to 7 days in the future).

## Monitoring

### Queue Metrics

Monitor these metrics in Azure Portal:

- **Message count**: Number of pending commands
- **Dequeue count**: Number of failed deliveries
- **Poison queue depth**: Number of permanently failed commands

### SQL Metrics

- **Command completion rate**: Success vs. failure
- **Average execution time**: Performance tracking
- **Command history**: Full audit trail

## Troubleshooting

### Commands not delivered

1. Check agent logs for JWT token issues
2. Verify queue connection string
3. Check queue exists: `pcav-{tenantId}-{agentId}`
4. Verify agent is polling `/commands/receive` endpoint

### Commands stuck in queue

1. Check poison queue: `pcav-poison-{tenantId}-{agentId}`
2. Review SQL for failed commands
3. Check agent logs for execution errors
4. Verify visibility timeout hasn't expired

### High queue depth

1. Check if agent is running
2. Verify agent can reach API endpoint
3. Check command execution time
4. Review rate limiting settings

## Migration from SQL Polling

The system is backward compatible. Agents will automatically use the new queue-based polling when available. The old SQL polling endpoint (`/commands/next`) remains functional but should not be used for new deployments.

## Security

- Queue names include tenant ID for isolation
- Agent JWT tokens required for all operations
- Commands validated against tenant membership
- SQL remains single source of truth for authorization
