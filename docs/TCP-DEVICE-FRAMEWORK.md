# TCP Device Framework — Developer Guide

## Overview

The **Shared TCP Device Framework** provides a reusable transport layer for integrating network-controlled AV devices into the ProdControlAV agent.

Rather than implementing connection management, read/write loops, and reconnection logic separately for each device type, all TCP infrastructure is centralised in `BaseTcpDeviceConnection`. Device-specific implementations supply only a response parser and optional protocol overrides.

---

## Architecture

```
Cloud API
    ↓
Agent polling (AgentService)
    ↓
Command queue (CommandService)
    ↓
Command router (ExecuteCommandAsync)
    ↓
DeviceConnectionPool             ← shared registry
    ↓
BaseTcpDeviceConnection          ← shared TCP transport
    ├── HyperDeckConnection      ← HyperDeck Ethernet Protocol
    ├── (future) AtemConnection
    └── (future) TelnetDeviceConnection
```

---

## Core Components

### `DeviceResponse` — `ProdControlAV.Agent.Models`

The standard response returned by every device connection.

| Property    | Type                             | Description                                   |
|-------------|----------------------------------|-----------------------------------------------|
| `Success`   | `bool`                           | `true` when `StatusCode` is in the 200–299 range |
| `StatusCode`| `int`                            | Protocol-level numeric status code             |
| `Message`   | `string`                         | Human-readable status text                    |
| `Data`      | `Dictionary<string,string>`      | Key/value fields from the response (case-insensitive keys) |

---

### `IDeviceConnection` — `ProdControlAV.Agent.Interfaces`

The contract every device connection must satisfy.

```csharp
public interface IDeviceConnection
{
    Task StartAsync(CancellationToken ct = default);

    Task<DeviceResponse> SendCommandAsync(
        string command,
        TimeSpan timeout,
        CancellationToken ct = default);

    bool IsConnected { get; }

    Task DisconnectAsync();
}
```

---

### `BaseTcpDeviceConnection` — `ProdControlAV.Agent.Services`

Abstract base class providing all TCP transport infrastructure.  Subclass it to add a new device type.

**Responsibilities handled by the base:**

- TCP socket management (connect / disconnect)
- Background read and write loops
- Command queue (`Channel<OutboundCommand>`)
- One-command-at-a-time serialisation (`SemaphoreSlim`)
- Response correlation (pending `TaskCompletionSource<DeviceResponse>`)
- Automatic reconnection with exponential backoff (500 ms → 10 s)
- Timeout handling (default 5 seconds)
- Structured logging (device type, host, port, command, status)

**Protocol hooks (override in subclass):**

| Method / Property       | Default                       | Purpose                                  |
|-------------------------|-------------------------------|------------------------------------------|
| `ParseResponseBlock`    | **abstract**                  | Convert raw line buffer → `DeviceResponse` |
| `IsEndOfBlock(line)`    | `string.IsNullOrEmpty(line)`  | Detect end of response block             |
| `FormatCommand(cmd)`    | `cmd + "\r\n"`                | Append line terminator before sending    |
| `StreamEncoding`        | `Encoding.ASCII`              | Character encoding for the stream        |
| `DeviceTypeName`        | `"Device"`                    | Label used in log messages               |

---

### `DeviceConnectionPool` — `ProdControlAV.Agent.Services`

Generic registry of active device connections keyed by `{deviceType}:{host}:{port}`.

```csharp
// Get or create a connection
IDeviceConnection conn = await pool.GetOrCreateAsync(
    deviceType : "hyperdeck",
    host       : "192.168.1.50",
    port       : 9993,
    factory    : () => new HyperDeckConnection("192.168.1.50", 9993, logger),
    ct         : cancellationToken);

// Remove and dispose
await pool.RemoveAsync("hyperdeck", "192.168.1.50", 9993);
```

The pool is thread-safe; concurrent callers for the same key are serialised — only one connection is created.

---

### `HyperDeckConnectionPool` — `ProdControlAV.Agent.Services`

Typed convenience wrapper over `DeviceConnectionPool` that returns strongly-typed `HyperDeckConnection` instances keyed by `{host}:{port}`.

```csharp
HyperDeckConnection conn =
    await _hyperDeckPool.GetOrCreateAsync("192.168.1.50", 9993, ct);

DeviceResponse response = await conn.SendCommandAsync("play", ct);
```

---

## Implementing a New Device Type

1. **Create the connection class** extending `BaseTcpDeviceConnection`:

```csharp
public sealed class MyDeviceConnection : BaseTcpDeviceConnection
{
    protected override string DeviceTypeName => "MyDevice";

    public MyDeviceConnection(string host, int port, ILogger logger)
        : base(host, port, logger) { }

    protected override DeviceResponse ParseResponseBlock(IReadOnlyList<string> lines)
    {
        // Parse device-specific ASCII/binary response into DeviceResponse
        if (lines.Count == 0) return new DeviceResponse();

        return new DeviceResponse
        {
            Success    = true,
            StatusCode = 200,
            Message    = lines[0],
            Data       = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
    }
}
```

2. **Register in `DeviceConnectionPool`** using the device-type label, e.g. `"mydevice"`.

3. **Handle the command type** in `CommandService.ExecuteCommandAsync` (add an `else if` branch for the new `CommandType`).

4. **Add unit tests** in `ProdControlAV.Tests` following the pattern in `DeviceConnectionFrameworkTests.cs`.

---

## Data Flow

### Command Execution (Happy Path)

```
1. API  →  agent polls /api/agents/commands/poll
2. CommandService.PollCommandsAsync parses CommandPayload
3. CommandService.ExecuteCommandAsync routes by CommandType
4. HyperDeckConnectionPool.GetOrCreateAsync returns a live connection
5. connection.SendCommandAsync(command, ct) is called
   a. SemaphoreSlim acquires the send lock (one command at a time)
   b. OutboundCommand queued on Channel
   c. WriteLoop picks up command, writes it to the TCP stream
   d. ReadLoop receives response lines, detects blank-line terminator
   e. HyperDeckConnection.ParseResponseBlock converts lines → DeviceResponse
   f. TaskCompletionSource<DeviceResponse> resolved with the response
6. CommandResult{Success, Message, Response} assembled from DeviceResponse
7. CommandService.RecordCommandHistoryAsync reports result back to the API
```

### Reconnection Path

```
1. ReadLoop catches an IOException or receives null (connection closed)
2. FailPendingCommand(exception) fails the outstanding TCS
3. ReconnectAsync starts with exponential backoff:
   500 ms → 1 s → 2 s → 5 s → 10 s (repeats at 10 s)
4. On success, a fresh ReadLoop task is started on the new stream
5. Pending callers of SendCommandAsync receive the IOException and may retry
```

---

## Configuration

Device connection parameters come from the `CommandPayload` delivered by the API:

| Field           | Description                                |
|-----------------|--------------------------------------------|
| `DeviceIp`      | Target device IP address                   |
| `DevicePort`    | TCP port (default 9993 for HyperDeck)      |
| `CommandType`   | `"HYPERDECK"`, `"ATEM"`, etc.              |
| `HyperDeckCommand` / `commandData` | Protocol command string  |

No static configuration is required in `appsettings.json` for device connections; all parameters are passed dynamically per command.

---

## Logging

All log entries include structured fields for `DeviceType`, `Host`, and `Port`.

| Event                      | Level     | Message pattern                                              |
|----------------------------|-----------|--------------------------------------------------------------|
| Device connected           | `Info`    | `{DeviceType} connected at {Host}:{Port}`                    |
| Device disconnected        | `Warning` | `{DeviceType} {Host}:{Port} closed the connection`           |
| Reconnect attempt          | `Info`    | `{DeviceType} reconnect attempt {Attempt} to {Host}:{Port} in {Delay}ms` |
| Command sent               | `Debug`   | `Sent {DeviceType} command: '{Command}' to {Host}:{Port}`    |
| Response received          | `Debug`   | `{DeviceType} response from {Host}:{Port}: {StatusCode} {Message}` |
| Unsolicited notification   | `Debug`   | `Unsolicited {DeviceType} update from {Host}:{Port}: ...`    |
| Command timeout            | `Warning` | `{DeviceType} command '{Command}' timed out at {Host}:{Port}` |
| Send error                 | `Error`   | `Error sending {DeviceType} command '{Command}' to {Host}:{Port}` |

---

## Security Considerations

- Device commands originate exclusively from the authenticated cloud API via the agent's JWT-authenticated polling loop.
- No device credentials are required or stored; the framework assumes operation within a trusted LAN segment.
- The agent uses a single outbound `HttpClient` with a configured connect timeout to prevent unbounded waits.
- Commands are queued through a `Channel` and serialised with a `SemaphoreSlim`; there is no mechanism for unauthenticated callers to inject commands.

---

## Testing

Tests live in `tests/ProdControlAV.Tests/`.

| Test class                       | What it covers                                                   |
|----------------------------------|------------------------------------------------------------------|
| `DeviceResponseTests`            | `DeviceResponse` model defaults and case-insensitive `Data`      |
| `BaseTcpDeviceConnectionTests`   | Constructor validation, `IsConnected`, round-trip via local TCP  |
| `IDeviceConnectionContractTests` | `HyperDeckConnection` satisfies `IDeviceConnection`              |
| `DeviceConnectionPoolTests`      | Pool deduplication, `RemoveAsync`, type isolation                 |
| `HyperDeckConnectionTests`       | `ParseBlock` unit tests; round-trip integration tests            |
| `HyperDeckConnectionPoolTests`   | Pool reuse for same key                                          |

Run with:

```bash
dotnet test --filter "HyperDeck|DeviceConnection|DeviceResponse|IDeviceConnectionContract|BaseTcpDeviceConnection"
```
