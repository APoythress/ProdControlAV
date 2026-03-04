# HyperDeck TCP Control

## Overview

The ProdControlAV Agent supports persistent TCP control of **Blackmagic HyperDeck** disk recorders and media servers using the [HyperDeck Ethernet Protocol](https://documents.blackmagicdesign.com/DeveloperManuals/HyperDeckEthernetProtocol.pdf). Unlike REST or Telnet commands, HyperDeck control uses a single long-lived TCP session that stays open between commands. The agent manages this session automatically — reconnecting if the device drops the connection — so you only need to define commands and trigger them.

---

## Prerequisites

| Requirement | Details |
|---|---|
| **HyperDeck firmware** | Any model supporting the Ethernet Control protocol |
| **Network access** | Agent host must be able to reach the HyperDeck's IP on **TCP port 9993** |
| **Device record in SQL DB** | The target HyperDeck must have a `Devices` table row with its IP address |
| **Agent running** | The ProdControlAV Agent must be online and authenticated |

> **Security note:** The HyperDeck Ethernet Protocol has no authentication. Keep HyperDeck devices on a private, isolated AV network and never expose port 9993 to the public internet. All control must go through the ProdControlAV Agent.

---

## How It Works

```
User triggers command in dashboard
          ↓
API enqueues command payload to Azure Queue Storage
          ↓
Agent polls queue and reads payload
          ↓
Agent routes "HYPERDECK" command type to HyperDeck transport
          ↓
HyperDeckConnectionPool.GetOrCreateAsync("ip", 9993)
   → reuses an existing connection, or opens a new one
          ↓
Command text sent over persistent TCP connection
          ↓
Read loop receives response block (terminated by blank line)
          ↓
Response parsed into StatusCode + Fields
          ↓
Result recorded in command history
```

The pool keeps **one TCP socket open per device** for the lifetime of the agent process. Subsequent commands to the same device reuse the same socket. If the device disconnects, the agent retries automatically with exponential backoff (500 ms → 1 s → 2 s → 5 s → 10 s).

---

## Creating a HyperDeck Command

### Required payload fields

When creating a command via `POST /api/commands` (or through the Commands UI), the `CommandType` must be `"HYPERDECK"` and the payload sent to the agent must include:

| Field | Type | Required | Description |
|---|---|---|---|
| `commandType` | string | ✅ | Must be `"HYPERDECK"` |
| `deviceIp` | string | ✅ | IP address of the HyperDeck device |
| `hyperDeckCommand` | string | ✅ | The raw HyperDeck protocol command to send (see table below) |
| `devicePort` | int | ❌ | TCP port — defaults to **9993** if omitted |

### Example payload JSON

```json
{
  "commandType": "HYPERDECK",
  "deviceIp": "192.168.1.50",
  "devicePort": 9993,
  "hyperDeckCommand": "play"
}
```

---

## Supported HyperDeck Commands

The following commands are recommended for the initial integration. Any valid HyperDeck Ethernet Protocol command string can be sent; the agent forwards it verbatim.

| Command | Description | Example response status |
|---|---|---|
| `play` | Start playback on the current slot | `200 ok` |
| `stop` | Stop playback or recording | `200 ok` |
| `record` | Start recording to the current slot | `200 ok` |
| `transport info` | Query current transport state | `208 transport info` |
| `slot info` | Query info about the current slot | `202 slot info` |
| `go to: clip id: {n}` | Jump to a specific clip | `200 ok` |
| `playrange set: {in} {out}` | Set play range by timecode | `200 ok` |
| `configuration` | Read device configuration | `211 configuration` |

> **Full protocol reference:** [HyperDeck Ethernet Protocol PDF](https://documents.blackmagicdesign.com/DeveloperManuals/HyperDeckEthernetProtocol.pdf)

---

## Response Format

After the agent executes a HyperDeck command, the result is stored in command history with the following JSON response body:

```json
{
  "statusCode": 200,
  "statusText": "ok",
  "fields": {}
}
```

For commands that return additional data (e.g. `transport info`):

```json
{
  "statusCode": 208,
  "statusText": "transport info",
  "fields": {
    "status": "play",
    "speed": "100",
    "slot id": "1",
    "clip id": "1",
    "timecode": "00:01:23:04"
  }
}
```

**Status code ranges:**

| Range | Meaning |
|---|---|
| `200–299` | Success — command accepted |
| `100–199` | Informational / unsolicited device update |
| `500+` | Error — command rejected or device fault |

The agent marks the command execution as **successful** when the status code is in the `200–299` range.

---

## Step-by-Step: Sending a Play Command

### 1. Ensure the device is registered

Confirm the HyperDeck has a row in the `Devices` table with a valid private IP address:

```sql
SELECT Id, Name, Ip, Type FROM Devices
WHERE Ip = '192.168.1.50';
```

If it does not exist, add it through the Devices UI or via SQL:

```sql
INSERT INTO Devices (Id, TenantId, Name, Ip, Type, CreatedAt)
VALUES (NEWID(), '<your-tenant-id>', 'HyperDeck Studio', '192.168.1.50', 'Video', SYSDATETIMEOFFSET());
```

### 2. Create the command definition

Use the **Commands** page in the dashboard or call the API directly:

```http
POST /api/commands
Authorization: Required (Cookie or JWT)
Content-Type: application/json

{
  "deviceId": "<device-guid>",
  "commandName": "HyperDeck Play",
  "description": "Start playback on HyperDeck Studio",
  "commandType": "HYPERDECK",
  "commandData": "play",
  "requireDeviceOnline": true
}
```

> **Note:** Use `commandData` to store the HyperDeck command string in the SQL definition. The API enqueue logic must map `commandData` → `hyperDeckCommand` in the queue payload. See [Payload Mapping](#payload-mapping) below.

### 3. Trigger the command

Click **Run** on the Commands page, or call:

```http
POST /api/commands/{commandId}/trigger
Authorization: Required (Cookie or JWT)
```

The response confirms the command was queued:

```json
{
  "success": true,
  "message": "Command queued for execution",
  "commandId": "...",
  "deviceName": "HyperDeck Studio"
}
```

### 4. Verify in command history

After the agent executes the command (within the next polling cycle, typically 5–30 seconds), the result appears in **Command History**:

- `success: true` — HyperDeck responded with `200 ok`
- `response` — JSON object with `statusCode`, `statusText`, and any `fields`

---

## Payload Mapping

The agent reads its instructions from the JSON payload stored in the Azure Queue message. The API enqueue layer must produce a payload with the following shape for HyperDeck commands:

```json
{
  "commandType": "HYPERDECK",
  "deviceIp": "<device ip from Devices table>",
  "devicePort": 9993,
  "hyperDeckCommand": "<value of commandData field>"
}
```

When the `Command.CommandType` is `"HYPERDECK"`, the enqueue logic should map:

| SQL `Command` field | Queue payload field |
|---|---|
| `CommandType` | `commandType` = `"HYPERDECK"` |
| `CommandData` | `hyperDeckCommand` |
| `Device.Ip` | `deviceIp` |
| `Device.Port` (or `9993`) | `devicePort` |

---

## Connection Lifecycle

The `HyperDeckConnectionPool` singleton maintains one connection per `{ip}:{port}` key for the entire lifetime of the agent process.

```
First command to 192.168.1.50:9993
  → Pool creates new HyperDeckConnection
  → TCP socket opened
  → ReadLoopAsync + WriteLoopAsync started as background tasks

Subsequent commands to same device
  → Pool returns existing connection (no new TCP handshake)
  → Command sent over existing socket
```

### Automatic reconnection

If the TCP connection drops (device rebooted, cable unplugged, etc.):

1. The read loop detects the disconnect
2. Any pending command is failed immediately with an `IOException`
3. Reconnect attempts begin with exponential backoff:

```
Attempt 1: wait 500 ms
Attempt 2: wait 1 s
Attempt 3: wait 2 s
Attempt 4: wait 5 s
Attempt 5+: wait 10 s (capped)
```

4. Once reconnected, the read loop restarts and new commands can flow normally

New commands arriving during a reconnect will queue in the outbound channel and be sent as soon as the connection is restored.

---

## Timeout Handling

Each command has a **5-second timeout**. If no response block is received within 5 seconds:

- A `TimeoutException` is raised
- The command is marked as failed in history with the timeout message
- The connection itself is **not** closed — subsequent commands can still succeed

Timeouts typically indicate the device is busy, the network has high latency, or the command was not recognised by the device.

---

## Logging

The agent logs the following events for HyperDeck connections:

| Event | Log level | Message example |
|---|---|---|
| Connection opened | `Information` | `Connected to HyperDeck at 192.168.1.50:9993` |
| Connection closed | `Warning` | `HyperDeck 192.168.1.50:9993 closed the connection` |
| Reconnect attempt | `Information` | `Reconnect attempt 2 to HyperDeck 192.168.1.50:9993 in 1000ms` |
| Command sent | `Debug` | `Sent HyperDeck command: 'play' to 192.168.1.50:9993` |
| Response received | `Debug` | `HyperDeck response from 192.168.1.50:9993: 200 ok` |
| Command completed | `Information` | `HyperDeck command 'play' completed with status 200 ok` |
| Command timeout | `Warning` | `HyperDeck command 'play' timed out at 192.168.1.50:9993` |
| Send error | `Error` | `Error sending HyperDeck command 'play' to 192.168.1.50:9993` |

Enable `Debug` level in `appsettings.json` to see individual command/response traffic:

```json
{
  "Logging": {
    "LogLevel": {
      "ProdControlAV.Agent.Services.HyperDeckConnection": "Debug"
    }
  }
}
```

---

## Troubleshooting

### Command fails immediately — "Missing required property: hyperDeckCommand"

The queue payload is missing the `hyperDeckCommand` field. Verify the API enqueue layer is mapping `commandData` → `hyperDeckCommand` correctly (see [Payload Mapping](#payload-mapping)).

### Command fails with "HyperDeck command execution failed: …"

Check the agent log for the full exception. Common causes:

- Device IP is unreachable from the agent host — verify network connectivity with `ping 192.168.1.50` from the agent machine
- Port 9993 blocked by a firewall — verify with `telnet 192.168.1.50 9993`
- Device is powered off or the Ethernet control feature is disabled in the HyperDeck setup menu

### Command times out after 5 seconds

- Verify the device is online and responding
- Check the HyperDeck setup menu — Ethernet control must be enabled
- Try a simpler command first (`transport info`) to confirm the connection works
- If timeouts are frequent, the device may not support the command sent; refer to the HyperDeck protocol documentation

### Agent shows repeated reconnect attempts in logs

The device is refusing or dropping the TCP connection. Check:

1. HyperDeck Ethernet control is enabled in the device's setup menu
2. No other client is connected to the device on port 9993 (some HyperDeck models limit concurrent connections)
3. The IP address in the `Devices` table matches the device's actual network address

### Same command works once but fails on the second attempt

The connection may have dropped after the first command. Check agent logs for disconnect/reconnect messages. The reconnect should happen automatically; if it does not, verify there are no network ACLs or firewalls that drop idle TCP connections.

---

## Architecture Reference

```
ProdControlAV.Agent
├── Services/
│   ├── CommandService.cs          ← routes "HYPERDECK" to ExecuteHyperDeckCommandAsync
│   ├── HyperDeckConnection.cs     ← persistent TCP connection + read/write loops + parser
│   └── HyperDeckConnectionPool.cs ← singleton pool keyed by "ip:port"
```

### Key classes

| Class | Responsibility |
|---|---|
| `HyperDeckConnectionPool` | Creates and caches `HyperDeckConnection` instances |
| `HyperDeckConnection` | Opens TCP socket; runs `ReadLoopAsync` / `WriteLoopAsync`; correlates commands with responses; reconnects on failure |
| `HyperDeckResponse` | Parsed response: `StatusCode`, `StatusText`, `Fields` dictionary |

---

## Future Enhancements

- **Device state tracking** — cache transport/slot state from unsolicited device updates for dashboard display
- **Telemetry** — publish real-time device state to the cloud API via WebSocket
- **Multi-slot support** — add slot-switching commands and slot state model
- **Command batching** — send multiple commands in sequence without releasing the send lock between them
- **Shared TCP framework** — reuse `HyperDeckConnection` architecture for other TCP-controlled AV devices (lighting consoles, matrix switchers, etc.)

---

## References

- [HyperDeck Ethernet Protocol (Blackmagic Design)](https://documents.blackmagicdesign.com/DeveloperManuals/HyperDeckEthernetProtocol.pdf)
- [ProdControlAV Command System](COMMAND_SYSTEM.md)
- [ProdControlAV ATEM Control Feature](ATEM_CONTROL_FEATURE.md)
