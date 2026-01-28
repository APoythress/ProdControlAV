# Agent Manual Update Polling Architecture - Quick Reference

## TL;DR

**The polling architecture is already implemented.** No code changes needed.

## How It Works (5-Step Flow)

```
1. WebApp User Action
   └─ User clicks "Apply Update" button
   └─ POST /api/agent/{agentId}/trigger-update

2. API Server
   └─ Creates UPDATE command
   └─ Stores in CommandQueue (Azure Table Storage)
   └─ Status: "Pending"

3. Agent Polling Loop (every 10 seconds)
   └─ POST /api/agents/commands/poll
   └─ JWT authentication
   └─ Receives UPDATE command

4. Agent Execution
   └─ CommandService detects UPDATE
   └─ Creates trigger file: /tmp/prodcontrolav-update-trigger
   └─ Marks command as "Processing"

5. Update Application
   └─ UpdateService detects trigger file
   └─ Downloads update from Azure Blob
   └─ Creates backup
   └─ Installs and restarts agent
```

## Key Files

| File | Purpose | Key Function |
|------|---------|--------------|
| `src/ProdControlAV.Agent/Services/AgentService.cs` | Polling loop | `RunCommandPollLoop()` - polls every 10s |
| `src/ProdControlAV.Agent/Services/CommandService.cs` | Command execution | `ExecuteCommandAsync()` - handles UPDATE |
| `src/ProdControlAV.Agent/Services/UpdateService.cs` | Update application | `ExecuteAsync()` - detects trigger, applies update |
| `src/ProdControlAV.API/Controllers/AgentsController.cs` | Poll endpoint | `PollCommandQueue()` - returns pending commands |
| `src/ProdControlAV.API/Controllers/AgentHealthController.cs` | Trigger endpoint | `TriggerAgentUpdate()` - enqueues UPDATE |

## Configuration

```json
// src/ProdControlAV.Agent/appsettings.json
{
  "Api": {
    "CommandPollIntervalSeconds": 10  // How often to poll (default: 10s)
  },
  "Update": {
    "CheckIntervalSeconds": 3600,     // How often to check appcast (default: 1 hour)
    "AutoInstall": true               // Auto-install updates when available
  }
}
```

## Benefits vs Alternative Approaches

### Polling (Current) ✅
```
Agent → API (outbound only)
  ✅ No exposed endpoints
  ✅ Simple firewall rules
  ✅ Works through NAT
  ✅ Commands persist in queue
```

### Push (Alternative) ❌
```
API → Agent (inbound required)
  ❌ Exposed HTTP endpoint
  ❌ Complex firewall rules
  ❌ NAT/proxy issues
  ❌ Security risks
```

## Timing

| Event | Time | Notes |
|-------|------|-------|
| User clicks "Apply Update" | T+0s | Command created in queue |
| Agent polls and detects | T+0-10s | Within poll interval |
| Update starts | T+10s | Immediate after detection |
| Update completes | T+2-3min | Download, backup, install, restart |

**Maximum delay**: 10 seconds (configurable via `CommandPollIntervalSeconds`)

## Error Handling

| Error Type | Behavior | Recovery |
|------------|----------|----------|
| 401 Unauthorized | Refresh JWT token | Retry (max 2 attempts) |
| Network failure | Log and continue | Next poll iteration |
| Command failure | Mark as failed | Retry (max 3 attempts) |
| Update failure | Rollback to backup | Previous version restored |

## Testing

### Quick Verification

```bash
# 1. Check Agent is polling
sudo journalctl -u prodcontrolav-agent -f | grep "Received.*commands"

# 2. Trigger update from Web UI
# Navigate to: https://yourapp.com/agent-health
# Click: "Apply Update" button

# 3. Watch for update detection (within 10 seconds)
sudo journalctl -u prodcontrolav-agent -f | grep "UPDATE command"

# 4. Monitor update progress
sudo journalctl -u prodcontrolav-agent -f | grep -E "(update|backup|download|install)"
```

### Expected Log Output

```
[AgentService] Received 1 commands to execute
[CommandService] Received UPDATE command, triggering agent update...
[CommandService] Update trigger signal created at: /tmp/prodcontrolav-update-trigger
[UpdateService] Manual update trigger detected, checking for updates immediately...
[UpdateService] Update available: Version 1.0.1 (current: 1.0.0)
[UpdateService] Creating backup at /opt/prodcontrolav/agent.2026-01-28_16-00-00
[UpdateService] Downloading update from Azure Blob Storage...
[UpdateService] Installing update to /opt/prodcontrolav/agent
[UpdateService] Update completed successfully, restarting...
```

## Performance

| Metric | Value |
|--------|-------|
| Poll frequency | 10 seconds (configurable) |
| API load per agent | 6 requests/minute (360/hour) |
| Latency (max) | 10 seconds (1 poll interval) |
| Queue storage cost | ~$0.01/month per agent |
| Network bandwidth | < 1 KB per poll (minimal) |

## Security

| Layer | Implementation |
|-------|----------------|
| Transport | HTTPS (TLS 1.2+) |
| Authentication | JWT tokens (8-hour expiry) |
| Authorization | Tenant isolation enforced |
| Update verification | Ed25519 signature validation |
| Agent security | No exposed endpoints |

## Related Documentation

- **Detailed Documentation**: `AGENT-UPDATE-POLLING-CONFIRMATION.md`
- **Architecture Diagrams**: `AGENT-UPDATE-ARCHITECTURE.md`
- **Command System**: `COMMAND_SYSTEM_SUMMARY.md`

## Status

✅ **Fully Implemented and Operational**

All features requested in the issue are already in production:
- Agent polling for commands
- UPDATE command handling
- Command queue persistence
- JWT authentication
- 401 retry logic
- Configurable intervals
- Server-side history recording

**No code changes required.** This documentation confirms the implementation.
