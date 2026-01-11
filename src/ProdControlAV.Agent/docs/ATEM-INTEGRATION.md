# ATEM Integration Guide

This document provides setup and usage instructions for the Blackmagic Design ATEM video switcher integration in the ProdControlAV Agent.

## Overview

The Agent supports control of ATEM Television Studios and compatible Blackmagic Design video switchers over the same LAN. The implementation uses the LibAtem library (LGPL-3.0) to communicate with ATEM devices via their proprietary UDP protocol.

## Multi-Tenant Architecture

**Important:** ATEM configuration follows the multi-tenant architecture of ProdControlAV:

- **Device-Specific Settings** (IP, Port, Name): Stored in the Devices table per tenant
  - Set `Type` to indicate ATEM capability (e.g., "ATEM" or "Video")
  - Set `AtemEnabled=true` to enable ATEM control for the device
  - Configure `Ip` and `Port` (default 9910) for the ATEM device
  - Optionally set `AtemTransitionDefaultRate` and `AtemTransitionDefaultType` per device

- **Agent-Level Settings** (appsettings.json): Global reconnection policies and defaults
  - Connection timeouts and reconnect backoff policies
  - State publishing intervals and coalescing settings
  - System-wide fallback defaults (used when device settings not specified)

This approach allows:
- Multiple tenants to manage their own ATEM devices independently
- Per-device ATEM configuration and monitoring
- Centralized management through the web interface
- Agent-level reliability policies consistent across all devices

## Supported Features

### Program/Preview Switching
- **Cut to Program**: Immediate transition to a specified input
- **Fade to Program**: Mix/fade transition with configurable rate
- **Set Preview**: Set preview input independently

### Macro Execution
- **List Macros**: Retrieve available macros from the ATEM
- **Run Macro**: Execute a macro by ID

### State Monitoring
- **Current Program Input**: Continuously track which input is on air
- **Current Preview Input**: Track preview selection
- **Connection State**: Monitor connection health and auto-reconnect

## Configuration

### Device Configuration (Per-Tenant, in Database)

ATEM devices should be configured through the web interface in the Devices table:

**Required Fields:**
- `Name`: Friendly name for the device (e.g., "Production ATEM")
- `Type`: Set to "ATEM" or "Video" to indicate ATEM capability
- `Ip`: IPv4 address of the ATEM device (e.g., "192.168.1.240")
- `Port`: ATEM port, typically 9910
- `TenantId`: Associated tenant ID

**ATEM-Specific Fields:**
- `AtemEnabled`: Set to `true` to enable ATEM control (default: false)
- `AtemTransitionDefaultType`: Default transition type ("mix" or "cut")
- `AtemTransitionDefaultRate`: Default transition rate in frames (e.g., 30 = 1 second @ 30fps)

**Example Device Configuration (via API/Web UI):**
```json
{
  "name": "Production ATEM",
  "type": "ATEM",
  "ip": "192.168.1.240",
  "port": 9910,
  "tenantId": "tenant-guid-here",
  "atemEnabled": true,
  "atemTransitionDefaultType": "mix",
  "atemTransitionDefaultRate": 30
}
```

### Agent Configuration (System-Wide, in appsettings.json)

The Agent's `appsettings.json` contains system-wide ATEM policies and fallback defaults:

```json
{
  "Atem": {
    "ConnectAuto": false,
    "ReconnectEnabled": true,
    "ReconnectMinDelaySeconds": 2,
    "ReconnectMaxDelaySeconds": 60,
    "ConnectTimeoutSeconds": 10,
    "StatePublishIntervalMs": 500,
    "StateEmitOnChangeOnly": true,
    "TransitionDefaultType": "mix",
    "TransitionDefaultRate": 30
  }
}
```

**Note:** The `Ip`, `Port`, and `Name` settings in appsettings.json are legacy single-device configurations. For multi-tenant deployments, these should be left empty or removed, and all device configuration should be done through the Devices table.

**Note:** The `Ip`, `Port`, and `Name` settings in appsettings.json are legacy single-device configurations. For multi-tenant deployments, these should be left empty or removed, and all device configuration should be done through the Devices table.

### Agent Configuration Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `ConnectAuto` | bool | false | Automatically connect on agent startup (applies to all ATEM devices) |
| `ReconnectEnabled` | bool | true | Enable automatic reconnection on failure |
| `ReconnectMinDelaySeconds` | int | 2 | Minimum wait before reconnect attempt |
| `ReconnectMaxDelaySeconds` | int | 60 | Maximum wait between reconnect attempts (exponential backoff cap) |
| `ConnectTimeoutSeconds` | int | 10 | Connection timeout |
| `StatePublishIntervalMs` | int | 500 | Minimum interval for publishing state updates (coalescing) |
| `StateEmitOnChangeOnly` | bool | true | Only emit state updates when values change |
| `TransitionDefaultType` | string | "mix" | System-wide fallback default transition type ("mix" or "cut") |
| `TransitionDefaultRate` | int | 30 | System-wide fallback default transition rate in frames (30 frames @ 30fps = 1 second) |

### Device Configuration Fields

| Field | Type | Description |
|-------|------|-------------|
| `Name` | string | Friendly name for the ATEM device |
| `Type` | string | Set to "ATEM" or "Video" to indicate capability |
| `Ip` | string | IPv4 address of the ATEM device |
| `Port` | int | UDP port for ATEM protocol (typically 9910) |
| `AtemEnabled` | bool | Enable ATEM control for this device |
| `AtemTransitionDefaultType` | string? | Device-specific default transition type (overrides agent setting) |
| `AtemTransitionDefaultRate` | int? | Device-specific default transition rate (overrides agent setting) |

## Command Contracts

All ATEM commands use the standard Agent command payload format with `commandType: "ATEM"`.

### Cut to Program

Performs an immediate cut transition to the specified input.

```json
{
  "commandType": "ATEM",
  "atemCommand": "CUT_TO_PROGRAM",
  "inputId": 1
}
```

**Parameters:**
- `inputId` (int, required): Input number (1-20 for most ATEMs)

### Fade to Program

Performs a mix/fade transition to the specified input.

```json
{
  "commandType": "ATEM",
  "atemCommand": "FADE_TO_PROGRAM",
  "inputId": 2,
  "transitionRate": 45
}
```

**Parameters:**
- `inputId` (int, required): Input number (1-20 for most ATEMs)
- `transitionRate` (int, optional): Transition rate in frames (defaults to configured default)

### Set Preview

Sets the preview input without affecting program output.

```json
{
  "commandType": "ATEM",
  "atemCommand": "SET_PREVIEW",
  "inputId": 3
}
```

**Parameters:**
- `inputId` (int, required): Input number (1-20 for most ATEMs)

### List Macros

Retrieves the list of available macros from the ATEM.

```json
{
  "commandType": "ATEM",
  "atemCommand": "LIST_MACROS"
}
```

**Response:**
```json
{
  "macros": [
    { "macroId": 0, "name": "Intro Sequence" },
    { "macroId": 1, "name": "Lower Third" }
  ]
}
```

### Run Macro

Executes a macro by its ID.

```json
{
  "commandType": "ATEM",
  "atemCommand": "RUN_MACRO",
  "macroId": 0
}
```

**Parameters:**
- `macroId` (int, required): Macro ID to execute

## State Events

The Agent publishes ATEM state changes through its normal telemetry pipeline:

### Connection State Changed

```json
{
  "deviceId": "atem-guid",
  "event": "ConnectionStateChanged",
  "state": "Connected",
  "timestamp": "2026-01-10T03:50:00Z"
}
```

**States:** `Disconnected`, `Connecting`, `Connected`, `Degraded`

### Program/Preview State

```json
{
  "deviceId": "atem-guid",
  "event": "ProgramPreviewState",
  "programInputId": 1,
  "previewInputId": 2,
  "timestamp": "2026-01-10T03:50:00Z"
}
```

## Network Requirements

- **Protocol**: UDP
- **Port**: 9910 (standard ATEM port)
- **Network**: ATEM must be reachable from the Agent (same LAN/VLAN or routed)
- **Firewall**: Allow outbound UDP on port 9910

On Linux systems (Raspberry Pi), the Agent may need the `CAP_NET_RAW` capability for low-level network access:

```bash
sudo setcap cap_net_raw+ep /opt/prodcontrolav/agent/ProdControlAV.Agent
```

## Supported ATEM Models

The implementation is designed to work with:
- ATEM Television Studio HD/4K
- ATEM Production Studio 4K
- ATEM 1 M/E, 2 M/E
- ATEM Mini series
- ATEM Constellation

**Note:** Some features may vary by model. The agent gracefully handles unsupported features.

## Troubleshooting

### Connection Issues

**Problem:** Agent cannot connect to ATEM  
**Solution:**
1. Verify ATEM IP address is correct in configuration
2. Check network connectivity: `ping 192.168.1.240`
3. Verify ATEM is powered on and responsive
4. Check firewall rules allow UDP traffic on port 9910
5. Review Agent logs for connection errors

**Problem:** Connection drops frequently  
**Solution:**
1. Check network stability and packet loss
2. Increase `ReconnectMaxDelaySeconds` for slower network recovery
3. Verify ATEM firmware is up to date
4. Check for network congestion or bandwidth issues

### Command Execution Issues

**Problem:** Commands fail with "device not ready"  
**Solution:**
- Wait for Agent to establish connection (check connection state)
- Verify `ConnectAuto` is true or manually initiate connection

**Problem:** "Invalid input ID" error  
**Solution:**
- Ensure input IDs are within valid range (typically 1-20)
- Check ATEM configuration for active inputs

### State Monitoring Issues

**Problem:** State updates are delayed  
**Solution:**
- Reduce `StatePublishIntervalMs` for more frequent updates
- Set `StateEmitOnChangeOnly` to false to receive all updates

## Security Considerations

- The Agent does not expose ATEM control directly to the internet
- All remote commands must be authenticated through the ProdControlAV API
- ATEM devices should be on a protected network segment
- No credentials are stored (ATEM protocol does not use authentication)

## LibAtem License Compliance

This feature uses LibAtem (LGPL-3.0). See [THIRD-PARTY-NOTICES.md](../../THIRD-PARTY-NOTICES.md) for full license information.

As required by the LGPL:
- The LibAtem library is dynamically linked via NuGet
- Users can replace LibAtem with a modified version by updating the package reference
- Source code for the Agent is available under MIT license
- LibAtem source is available at https://github.com/LibAtem/LibAtem

## Integration Testing

### Manual Testing with Real Hardware

To test ATEM integration with actual hardware:

1. **Setup:**
   - Configure ATEM IP in `appsettings.json`
   - Set `ConnectAuto: true`
   - Start the Agent

2. **Verify Connection:**
   - Check logs for "Successfully connected to ATEM"
   - Monitor connection state events

3. **Test Commands:**
   - Send Cut to Program command for input 1
   - Verify ATEM switches to input 1
   - Send Fade to Program for input 2
   - Verify smooth transition occurs
   - Send Set Preview for input 3
   - Verify preview updates on ATEM

4. **Test Reconnection:**
   - Disconnect ATEM (power off or network disconnect)
   - Verify Agent detects disconnection
   - Reconnect ATEM
   - Verify Agent automatically reconnects

5. **Test Macros:**
   - Create macros on ATEM
   - Send List Macros command
   - Send Run Macro command
   - Verify macro executes on ATEM

### Expected Results

- Connection established within 10 seconds
- Commands execute with <100ms latency
- State updates received within 500ms
- Reconnection successful after temporary network loss
- No memory leaks during 24-hour stability test

## Future Enhancements

Features not currently implemented but possible in future versions:

- Media pool management
- SuperSource configuration
- Detailed keyer control
- Audio mixer control
- Streaming/recording control
- Tally routing
- Multiview layout changes
- Transition type selection (wipe, dip, etc.)

---

*Last Updated: January 10, 2026*
