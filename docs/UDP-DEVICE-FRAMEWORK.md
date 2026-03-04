# UDP Device Framework — Developer & Operator Guide

## Overview

The **UDP Device Framework** provides a reusable transport layer for integrating UDP-based AV devices — in particular the **Blackmagic ATEM** video switcher family — into the ProdControlAV agent.

All UDP infrastructure (session lifecycle, receive loop, command queuing, response correlation, handshake, keepalive, reliability/ACK scaffolding, reconnection with backoff) is centralised in `BaseUdpDeviceConnection`. Device-specific subclasses supply only the datagram encoder/decoder and optional protocol overrides.

---

## Architecture

```
Cloud API (ProdControlAV.API)
    ↓  JWT-authenticated HTTP polling
Agent (ProdControlAV.Agent)
    ↓  CommandService.PollCommandsAsync
CommandPayload (deserialized from Table Storage queue)
    ↓  CommandService.ExecuteCommandAsync (routes by CommandType)
ATEM path → ExecuteAtemCommandAsync → AtemConnectionManager
    ↓  GetOrCreateConnectionAsync  (device-keyed connection pool)
BaseUdpDeviceConnection subclass (e.g. AtemUdpConnection — future)
    ↓  SendCommandAsync
UdpClient socket  →  physical ATEM device (UDP, default port 9910)
```

---

## User-Facing Command Flow

### Step 1 — Create or configure a command in the UI

Navigate to **Commands** in the ProdControlAV dashboard and create (or select) an ATEM command.  The relevant fields for a UDP/ATEM command are:

| UI Field | Value |  Description |
|---|---|---|
| **Device** | (select ATEM device) | Targets the registered ATEM switcher |
| **Command Type** | `ATEM` | Routes to the ATEM execution branch |
| **ATEM Function** | `CutToProgram` / `FadeToProgram` / `SetPreview` / `SetAuxAux1` | Selects the ATEM operation |
| **ATEM Input ID** | `1` – `20` | The ATEM input number to switch to |
| **ATEM Transition Rate** | `30` (frames) | Only used for `FadeToProgram`; defaults to 30 frames |

> The device's IP address, port, and tenant ID are resolved automatically from the registered device record — users do not type them manually.

### Step 2 — Trigger the command

Click **Run** on the command's trigger modal.  This:

1. Validates the device is online (if `RequireDeviceOnline` is checked).
2. Writes a **CommandQueue entry** to Azure Table Storage.
3. Returns immediately with `"Command queued for execution"`.

### Step 3 — Agent picks up the command (automatic)

The ProdControlAV agent running on the Raspberry Pi polls `/api/agents/commands/poll` on a regular interval.  When a command is returned:

1. **`PollCommandsAsync`** deserialises the JSON payload into a `CommandPayload`.
2. **`ExecuteCommandAsync`** routes based on `CommandType == "ATEM"`.
3. **`ExecuteAtemCommandAsync`** validates required fields and calls the appropriate `AtemConnectionManager` method.
4. `AtemConnectionManager` calls `BaseUdpDeviceConnection.SendCommandAsync` on the logical UDP session.
5. The result (`CommandResult`) is reported back to the API via `RecordCommandHistoryAsync`.

---

## CommandQueue — Exact Data Shape

### Azure Table Storage Record

```
PartitionKey : {tenantId-lowercase-guid}
RowKey       : {commandId}_{queuedUtcISO8601}

Example:
  PartitionKey = "a1b2c3d4-0000-0000-0000-000000000000"
  RowKey       = "f7e6d5c4-1234-5678-abcd-ef0123456789_2026-03-04T19:30:00.000Z"
```

### Table Storage Properties

| Property | Type | Example Value | Required |
|---|---|---|---|
| `CommandId` | string (GUID) | `"f7e6d5c4-1234-5678-abcd-ef0123456789"` | ✅ |
| `DeviceId` | string (GUID) | `"aabbccdd-1111-2222-3333-444455556666"` | ✅ |
| `CommandName` | string | `"Cut to Camera 2"` | Optional |
| `CommandType` | string | `"ATEM"` | ✅ |
| `CommandData` | string | `null` (not used for ATEM) | — |
| `DeviceIp` | string | `"192.168.1.240"` | ✅ |
| `DevicePort` | int | `9910` | ✅ (defaults to 9910) |
| `QueuedUtc` | DateTimeOffset | `2026-03-04T19:30:00Z` | ✅ |
| `QueuedByUserId` | string | `"user-guid"` | Optional |
| `Status` | string | `"Pending"` | ✅ |

### Nested Command Payload JSON (stored in the API response `command.payload`)

The agent receives this from the `/api/agents/commands/poll` endpoint.  The `payload` property is a **JSON string** containing:

```json
{
  "commandId":        "f7e6d5c4-1234-5678-abcd-ef0123456789",
  "deviceId":         "aabbccdd-1111-2222-3333-444455556666",
  "deviceIp":         "192.168.1.240",
  "devicePort":       9910,
  "deviceType":       "ATEM",
  "commandType":      "ATEM",
  "atemFunction":     "CutToProgram",
  "atemInputId":      2,
  "atemTransitionRate": null,
  "attemptCount":     0,
  "statusEndpoint":   null,
  "monitorRecordingStatus": false
}
```

For a `FadeToProgram` command:

```json
{
  "commandId":        "a9b8c7d6-0001-0002-0003-000400050006",
  "deviceId":         "aabbccdd-1111-2222-3333-444455556666",
  "deviceIp":         "192.168.1.240",
  "devicePort":       9910,
  "deviceType":       "ATEM",
  "commandType":      "ATEM",
  "atemFunction":     "FadeToProgram",
  "atemInputId":      3,
  "atemTransitionRate": 60,
  "attemptCount":     0,
  "statusEndpoint":   null,
  "monitorRecordingStatus": false
}
```

### Deserialized `CommandPayload` object (C# — what the agent actually works with)

```csharp
new CommandPayload
{
    CommandId            = Guid.Parse("f7e6d5c4-1234-5678-abcd-ef0123456789"),
    DeviceId             = Guid.Parse("aabbccdd-1111-2222-3333-444455556666"),
    DeviceIp             = "192.168.1.240",
    DevicePort           = 9910,
    DeviceType           = "ATEM",
    CommandType          = "ATEM",
    AtemFunction         = "CutToProgram",   // case-insensitive in router
    AtemInputId          = 2,
    AtemTransitionRate   = null,             // 30 fps used as default
    AttemptCount         = 0,
    HyperDeckCommand     = null,
    StatusEndpoint       = null,
    MonitorRecordingStatus = false
}
```

---

## Supported ATEM Functions (`atemFunction` values)

| `atemFunction` value | Description | Required fields | Optional fields |
|---|---|---|---|
| `CutToProgram` | Instant hard cut to program input | `atemInputId` | — |
| `FadeToProgram` | Auto mix/fade to program input | `atemInputId` | `atemTransitionRate` (default 30) |
| `SetPreview` | Set preview bus input | `atemInputId` | — |
| `SetAuxAux1` | Route input to Aux 1 output | `atemInputId` | — |
| `SetAuxAux2` | Route input to Aux 2 output | `atemInputId` | — |
| `SetAuxAux3` | Route input to Aux 3 output | `atemInputId` | — |
| `FadeAuxAux1` | Fade aux 1 to input | `atemInputId` | — |
| `FadeAuxAux2` | Fade aux 2 to input | `atemInputId` | — |
| `FadeAuxAux3` | Fade aux 3 to input | `atemInputId` | — |
| `RunMacro` | Run an ATEM macro by ID | `atemInputId` (as macro ID) | — |

---

## UDP Transport Internals — What Happens After `SendCommandAsync`

When a command reaches `BaseUdpDeviceConnection.SendCommandAsync`:

```
1. SemaphoreSlim acquired                    → only one command in-flight at a time

2. OutboundUdpCommand written to Channel    → { Command: "CutToProgram:2", Response: TCS<DeviceResponse> }

3. SendLoop reads from Channel
   a. BuildDatagramFromCommand(command, ctx) → subclass encodes string → byte[]
   b. _pendingResponse = cmd.Response        → registers TCS before sending (race-safe)
   c. If UsesReliability → store in _pendingAcks[seq]
   d. UdpClient.SendAsync(datagram)          → datagram sent over network
   e. ProtocolContext.OutboundSequence++

4. ReceiveLoop (concurrent)
   a. UdpClient.ReceiveAsync()               → blocks until datagram arrives
   b. DispatchDatagramAsync(rx)
      ├─ If handshake in progress → signal _handshakeTcs
      ├─ If IsKeepAliveResponse   → discard silently
      ├─ If IsAckDatagram         → call ApplyAck, remove from _pendingAcks
      ├─ If UsesReliability       → send ACK back to device
      └─ TryParseDeviceResponse   → DeviceResponse{ Success, StatusCode, Message, Data }
   c. DeliverResponse → TCS.TrySetResult(response)

5. SendCommandAsync awaits TCS with timeout
   ├─ Response received   → return DeviceResponse to caller ✅
   └─ Timeout elapsed     → throw TimeoutException ❌

6. SemaphoreSlim released
```

### Session Handshake (protocols that require it, e.g. ATEM)

```
StartAsync called
    ↓
InitialiseSessionAsync
    ↓  UdpClient created + connected to device endpoint
    ↓  if RequiresHandshake == true:
         SendHandshakeAsync() → send "hello" datagram
         ReceiveLoop starts   → first datagram checked with IsHandshakeResponse()
         ApplyHandshakeResponse() → extract SessionId, etc.
         state = Connected ✅
    ↓  if RequiresHandshake == false:
         state = Connected immediately ✅

ReceiveLoop + SendLoop + KeepAliveLoop now running in background
```

### Reconnection on Failure

```
ReceiveLoop catches exception  OR  keepalive times out
    ↓
ReconnectAsync
    ↓  state = Disconnected
    ↓  FailPendingCommand(IOException)    → outstanding TCS fails with exception
    ↓  DrainOutboundChannel(IOException)  → queued-but-unsent commands fail
    ↓  Exponential backoff loop:
         attempt 0 → wait 500 ms
         attempt 1 → wait 1 s
         attempt 2 → wait 2 s
         attempt 3 → wait 5 s
         attempt 4+ → wait 10 s  (cap)
    ↓  UdpClient re-created
    ↓  Handshake re-run (if RequiresHandshake)
    ↓  new ReceiveLoop started
    ↓  state = Connected ✅
```

---

## Session State Machine

```
         ┌──────────┐
         │Disconnected│◄──────────── DisconnectAsync()
         └─────┬────┘               or ReconnectAsync starts
               │ StartAsync() / ReconnectAsync()
               ▼
         ┌──────────┐
         │Connecting │   ← handshake in progress (if required)
         └─────┬────┘
               │ handshake success (or no handshake required)
               ▼
         ┌──────────┐
         │ Connected │   ← commands can be sent, keepalive running
         └─────┬────┘
               │ socket error / keepalive miss / repeated failure
               ▼
         ┌──────────┐
         │  Faulted  │   ← backing off, will retry
         └──────────┘
```

`IsConnected` returns `true` only in the `Connected` state.

---

## Logging Reference

All log entries include structured fields for `{DeviceType}`, `{Host}`, and `{Port}`.

| Event | Level | Message pattern |
|---|---|---|
| Session starting | `Info` | `{DeviceType} session starting for {Host}:{Port}` |
| Session ready (no handshake) | `Info` | `{DeviceType} session ready (no handshake required) for {Host}:{Port}` |
| Handshake started | `Info` | `{DeviceType} starting handshake with {Host}:{Port}` |
| Handshake completed | `Info` | `{DeviceType} handshake completed with {Host}:{Port}` |
| Session disconnected | `Info` | `{DeviceType} session disconnected ({Host}:{Port})` |
| Reconnect attempt | `Info` | `{DeviceType} reconnect attempt {Attempt} to {Host}:{Port} in {Delay}ms` |
| Reconnected | `Info` | `{DeviceType} reconnected to {Host}:{Port}` |
| Keepalive sent | `Debug` | `{DeviceType} keepalive sent to {Host}:{Port}` |
| Keepalive failure | `Warning` | `{DeviceType} keepalive send failed for {Host}:{Port}` |
| Datagram received | `Debug` | `Received {Bytes} bytes from {DeviceType} {Host}:{Port}` |
| Command enqueued | `Debug` | `{DeviceType} command enqueued for {Host}:{Port}: '{Command}'` |
| Datagram sent | `Debug` | `Sent {DeviceType} command ({Bytes} bytes) to {Host}:{Port}` |
| Response delivered | `Debug` | `{DeviceType} response from {Host}:{Port}: {StatusCode} {Message}` |
| Unsolicited datagram | `Debug` | `Unsolicited {DeviceType} update from {Host}:{Port}: {StatusCode} {Message}` |
| Command timeout | `Warning` | `{DeviceType} command timed out at {Host}:{Port}: '{Command}'` |
| Receive error | `Error` | `Receive error from {DeviceType} {Host}:{Port}` |
| Dispatch error | `Error` | `Error dispatching datagram from {DeviceType} {Host}:{Port}` |
| Send error | `Error` | `Error sending {DeviceType} command to {Host}:{Port}` |
| Reconnect failed | `Warning` | `{DeviceType} reconnect attempt {Attempt} to {Host}:{Port} failed` |

---

## Testing

### Running the UDP framework tests

```bash
dotnet test --filter "BaseUdpDevice"
```

Expected output: **15 tests pass**.

### Test classes

| Test class | What it covers |
|---|---|
| `BaseUdpDeviceConnectionConstructorTests` | Null/empty host, out-of-range port, initial `IsConnected == false` |
| `BaseUdpDeviceConnectionContractTests` | Implements `IDeviceConnection` |
| `BaseUdpDeviceConnectionLifecycleTests` | `StartAsync` sets `IsConnected`, `DisconnectAsync` clears it, `DisposeAsync` is clean |
| `BaseUdpDeviceConnectionSendReceiveTests` | Round-trip with local UDP echo server, explicit timeout overload |
| `BaseUdpDeviceConnectionTimeoutTests` | No-response scenario resolves within expected wall time |
| `BaseUdpDeviceConnectionDisconnectTests` | Pending command fails immediately on `DisconnectAsync` |
| `BaseUdpDeviceConnectionRobustnessTests` | Parse exception does not crash the receive loop |
| `ReceivedDatagramTests` | Default values, data/endpoint storage |
| `UdpProtocolContextTests` | Default sequence numbers and session ID |

### Minimal integration test using a local UDP echo server

```csharp
// 1. Start a local UDP server
using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
int port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;

// 2. Create and start the connection (no handshake in this example)
await using var conn = new StubUdpConnection("127.0.0.1", port, NullLogger.Instance);
await conn.StartAsync();

// 3. Run a command from a background "server"
var serverTask = Task.Run(async () =>
{
    var result = await server.ReceiveAsync();
    var reply = Encoding.UTF8.GetBytes("OK command-accepted");
    await server.SendAsync(reply, reply.Length, result.RemoteEndPoint);
});

// 4. Assert the response
var response = await conn.SendCommandAsync("CutToProgram:2", TimeSpan.FromSeconds(5));
Assert.True(response.Success);
Assert.Equal("OK command-accepted", response.Message);
```

### Simulating a CommandPayload for ATEM command testing

Use these exact objects in unit tests that exercise `ExecuteAtemCommandAsync` directly:

```csharp
// CutToProgram (input 2)
var cutPayload = new CommandPayload
{
    CommandId          = Guid.NewGuid(),
    DeviceId           = Guid.NewGuid(),
    DeviceIp           = "192.168.1.240",
    DevicePort         = 9910,
    CommandType        = "ATEM",
    AtemFunction       = "CutToProgram",
    AtemInputId        = 2,
    AtemTransitionRate = null,
    AttemptCount       = 0
};

// FadeToProgram (input 3, 60 frames)
var fadePayload = new CommandPayload
{
    CommandId          = Guid.NewGuid(),
    DeviceId           = Guid.NewGuid(),
    DeviceIp           = "192.168.1.240",
    DevicePort         = 9910,
    CommandType        = "ATEM",
    AtemFunction       = "FadeToProgram",
    AtemInputId        = 3,
    AtemTransitionRate = 60,
    AttemptCount       = 0
};

// SetPreview (input 4)
var previewPayload = new CommandPayload
{
    CommandId    = Guid.NewGuid(),
    DeviceId     = Guid.NewGuid(),
    DeviceIp     = "192.168.1.240",
    DevicePort   = 9910,
    CommandType  = "ATEM",
    AtemFunction = "SetPreview",
    AtemInputId  = 4,
    AttemptCount = 0
};

// SetAuxAux1 (route input 1 to Aux 1)
var auxPayload = new CommandPayload
{
    CommandId    = Guid.NewGuid(),
    DeviceId     = Guid.NewGuid(),
    DeviceIp     = "192.168.1.240",
    DevicePort   = 9910,
    CommandType  = "ATEM",
    AtemFunction = "SetAuxAux1",
    AtemInputId  = 1,
    AttemptCount = 0
};
```

### Raw JSON payload (as delivered from the API to the agent)

This is the JSON string stored inside `command.payload` from `/api/agents/commands/poll`.  Use it to verify end-to-end JSON deserialization in integration tests:

```json
{
  "command": {
    "commandId": "f7e6d5c4-1234-5678-abcd-ef0123456789",
    "deviceId":  "aabbccdd-1111-2222-3333-444455556666",
    "payload":   "{\"deviceIp\":\"192.168.1.240\",\"devicePort\":9910,\"deviceType\":\"ATEM\",\"commandType\":\"ATEM\",\"atemFunction\":\"CutToProgram\",\"atemInputId\":2,\"atemTransitionRate\":null,\"attemptCount\":0,\"statusEndpoint\":null,\"monitorRecordingStatus\":false}"
  }
}
```

---

## Implementing a New UDP Device (Subclass Guide)

```csharp
public sealed class AtemUdpConnection : BaseUdpDeviceConnection
{
    protected override string DeviceTypeName => "ATEM";

    // ATEM requires a handshake on connect
    protected override bool RequiresHandshake => true;

    // ATEM requires periodic keepalive packets (~every 500 ms)
    protected override bool RequiresKeepAlive => true;
    protected override TimeSpan KeepAliveInterval => TimeSpan.FromMilliseconds(500);

    // ATEM uses ACK-based reliability
    protected override bool UsesReliability => true;

    public AtemUdpConnection(string host, int port, ILogger logger)
        : base(host, port, logger) { }

    // 1. Encode a logical command string into binary ATEM datagram bytes
    protected override byte[] BuildDatagramFromCommand(string command, UdpProtocolContext ctx)
    {
        // TODO: encode per ATEM binary protocol
        // ctx.OutboundSequence is the packet sequence number
        // ctx.SessionId is the session ID obtained during handshake
        return Array.Empty<byte>();
    }

    // 2. Decode an inbound datagram into a DeviceResponse
    protected override bool TryParseDeviceResponse(ReceivedDatagram rx, out DeviceResponse response)
    {
        // TODO: parse per ATEM binary protocol
        response = new DeviceResponse { Success = true, StatusCode = 200 };
        return true;
    }

    // 3. Handshake — identify the ATEM "hello" ack packet
    protected override bool IsHandshakeResponse(ReceivedDatagram rx)
    {
        // TODO: check ATEM hello-ack flag in rx.Data header
        return false;
    }

    protected override void ApplyHandshakeResponse(ReceivedDatagram rx)
    {
        // TODO: extract ATEM session ID from rx.Data and store in ProtocolContext.SessionId
    }

    // 4. Keepalive — build an ATEM ping packet
    protected override async Task SendKeepAliveAsync(CancellationToken ct)
    {
        // TODO: send ATEM keepalive datagram
        await Task.CompletedTask;
    }

    // 5. ACK — build and identify ACK packets
    protected override byte[] BuildAckDatagram(UdpProtocolContext ctx, ReceivedDatagram rx)
    {
        // TODO: build ATEM ACK for the received packet sequence
        return Array.Empty<byte>();
    }

    protected override bool IsAckDatagram(ReceivedDatagram rx)
    {
        // TODO: check ATEM packet flags for ACK bit
        return false;
    }
}
```

Once the subclass is ready, register it in `DeviceConnectionPool`:

```csharp
IDeviceConnection conn = await pool.GetOrCreateAsync(
    deviceType : "atem",
    host       : "192.168.1.240",
    port       : 9910,
    factory    : () => new AtemUdpConnection("192.168.1.240", 9910, logger));
```

---

## Configuration

Device connection parameters for UDP commands come entirely from the `CommandPayload` delivered by the API.  No static configuration in `appsettings.json` is required for the transport layer itself.

| `CommandPayload` field | Purpose | Default |
|---|---|---|
| `DeviceIp` | Target device IP or hostname | (required) |
| `DevicePort` | Target UDP port | `9910` for ATEM |
| `CommandType` | Must be `"ATEM"` to reach UDP path | — |
| `AtemFunction` | The ATEM operation name | (required) |
| `AtemInputId` | The ATEM source input number | (required) |
| `AtemTransitionRate` | Transition rate in frames (FadeToProgram only) | `30` |

---

## Security Notes

- All commands originate from the JWT-authenticated cloud API; the agent cannot receive commands from unauthenticated sources.
- Device IP addresses are validated server-side (RFC 1918 private ranges only) before a command is queued.
- The UDP base class does not store credentials or secrets; session IDs are held in memory only for the lifetime of the connection.
- The `Channel` + `SemaphoreSlim` design prevents concurrent command injection even if the agent is called from multiple threads.
