# Agent Manual Testing Guide

## Prerequisites
The Agent endpoint configuration has been fixed. To test the complete Agent workflow including device monitoring, follow these steps:

## 1. Database Setup (Azure SQL Database)

Add test data to your Azure SQL Database:

### Create Test Tenant
```sql
INSERT INTO Tenants (Id, Name, Slug, CreatedUtc) 
VALUES ('12345678-1234-1234-1234-123456789abc', 'Test Tenant', 'test', GETUTCDATE());
```

### Create Test Agent
```sql
INSERT INTO Agents (Id, TenantId, Name, AgentKeyHash, LastSeenUtc) 
VALUES (
    '87654321-4321-4321-4321-cba987654321', 
    '12345678-1234-1234-1234-123456789abc',
    'Test Agent',
    'E1B85B27D6BCB05846C18E6A48F118E89F0C0587140DE9FB3359F8370D0DBA08',
    GETUTCDATE()
);
```

### Create Test Devices
```sql
-- Google DNS (should respond to ping)
INSERT INTO Devices (Id, TenantId, Name, Model, Brand, Type, AllowTelNet, Ip, Port, Status) 
VALUES (
    '11111111-1111-1111-1111-111111111111',
    '12345678-1234-1234-1234-123456789abc',
    'Google DNS Primary', 'DNS Server', 'Google', 'DNS', 0, '8.8.8.8', 53, 1
);

-- Google DNS Secondary (should respond to ping)  
INSERT INTO Devices (Id, TenantId, Name, Model, Brand, Type, AllowTelNet, Ip, Port, Status)
VALUES (
    '22222222-2222-2222-2222-222222222222',
    '12345678-1234-1234-1234-123456789abc', 
    'Google DNS Secondary', 'DNS Server', 'Google', 'DNS', 0, '8.8.4.4', 53, 1
);

-- Non-existent IP (should not respond to ping)
INSERT INTO Devices (Id, TenantId, Name, Model, Brand, Type, AllowTelNet, Ip, Port, Status)
VALUES (
    '33333333-3333-3333-3333-333333333333',
    '12345678-1234-1234-1234-123456789abc',
    'Test Offline Device', 'Test Device', 'Test', 'Test', 0, '192.168.254.254', 80, 0
);
```

## 2. Agent Configuration

Set the API key environment variable:
```bash
export PRODCONTROL_AGENT_APIKEY="12345678901234567890123456789012"
```

The Agent `appsettings.json` should already be configured with correct endpoints:
```json
{
  "Api": {
    "BaseUrl": "https://localhost:5001/api",
    "DevicesEndpoint": "/agents/devices",
    "StatusEndpoint": "/agents/status", 
    "HeartbeatEndpoint": "/agents/heartbeat",
    "CommandsEndpoint": "/agents/commands/next",
    "CommandCompleteEndpoint": "/agents/commands/complete"
  }
}
```

## 3. Testing Steps

### Start the API Server
```bash
cd src/ProdControlAV.API
dotnet run
```

Expected output:
- `Now listening on: https://localhost:5001`
- No errors during startup

### Start the Agent
```bash 
cd src/ProdControlAV.Agent
PRODCONTROL_AGENT_APIKEY="12345678901234567890123456789012" dotnet run
```

### Expected Agent Logs

**Successful Authentication:**
```
info: ProdControlAV.Agent.Services.DeviceSource[0]
      Fetched X devices from API
```

**Device Monitoring:**
```
info: ProdControlAV.Agent.Services.StatusPublisher[0]
      State change posted: Google DNS Primary 8.8.8.8 -> ONLINE
info: ProdControlAV.Agent.Services.StatusPublisher[0]  
      State change posted: Test Offline Device 192.168.254.254 -> OFFLINE
```

**Heartbeat Success:**
```
debug: ProdControlAV.Agent.Services.StatusPublisher[0]
       Heartbeat sent successfully
```

**Command Polling:**
```
info: System.Net.Http.HttpClient.ICommandService.LogicalHandler[100]
      Start processing HTTP request POST https://localhost:5001/agents/commands/next
```

## 4. Validation Checklist

- [ ] Agent starts without errors
- [ ] Agent successfully authenticates (no 401 Unauthorized errors)  
- [ ] Agent fetches device list from `/agents/devices`
- [ ] Agent pings devices every 5 seconds (default IntervalMs)
- [ ] Agent posts status updates to `/agents/status` 
- [ ] Agent sends heartbeats every 60 seconds to `/agents/heartbeat`
- [ ] Agent polls for commands every 10 seconds from `/agents/commands/next`
- [ ] Online devices (8.8.8.8, 8.8.4.4) show as ONLINE
- [ ] Offline devices (192.168.254.254) show as OFFLINE

## 5. Troubleshooting

**401 Unauthorized Errors:** 
- Verify API key matches hash in database
- Check Agent exists in database with correct TenantId

**No Device Pinging:**
- Verify devices exist in database for the agent's TenantId
- Check device fetch logs for API errors

**SSL Certificate Errors (Development):**
- Expected for self-signed certificates
- Agent will continue retrying
- Use `dotnet dev-certs https --trust` if needed

## API Key Details
- **Plain text key:** `12345678901234567890123456789012` (32 characters)
- **SHA256 hash:** `E1B85B27D6BCB05846C18E6A48F118E89F0C0587140DE9FB3359F8370D0DBA08`
- **Environment variable:** `PRODCONTROL_AGENT_APIKEY`