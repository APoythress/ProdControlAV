# Agent Command Error Handling Guide

## Overview
This guide explains the improved error handling for agent commands and how to monitor failures in your ProdControlAV deployment.

## What Was Fixed

### Issue
Agents were crashing when devices returned malformed HTTP responses like:
```
System.Net.Http.HttpRequestException: Received an invalid status line: '500 connection info:'.
```

### Solution
The agent now gracefully handles devices with non-compliant HTTP implementations by:
1. Using HTTP/1.1 protocol explicitly (better compatibility with embedded devices)
2. Catching malformed HTTP responses and logging detailed error information
3. Recording all failures in command history for dashboard visibility

## Monitoring Command Failures

### Agent Health Dashboard
Navigate to **Agent Health Dashboard** (`/agent-health`) to view:

1. **Failed Commands (48h)**: Shows count of unsuccessful commands in last 48 hours
2. **Recent Errors**: Displays top 5 most recent errors
3. **Error Details**: Click "View" button to see full error messages with timestamps

### Error Information Included
Each error log contains:
- **Device IP and Port**: Exact device that failed
- **Command Details**: HTTP method and endpoint
- **Error Type**: Timeout, malformed response, or connection error
- **Timestamp**: When the error occurred

### Example Error Messages

**Malformed HTTP Response:**
```
Device at 10.10.30.235:9993 returned malformed HTTP response. 
The device may not properly support HTTP protocol. 
Error: Received an invalid status line: '500 connection info:'.
```

**Connection Timeout:**
```
Request to 10.10.30.235:9993 timed out after 5 seconds
```

**General Communication Error:**
```
Error communicating with device at 10.10.30.235:9993: 
No connection could be made because the target machine actively refused it.
```

## Troubleshooting Device Communication Issues

### Malformed HTTP Response
**Symptom**: Error mentions "malformed HTTP response" or "invalid status line"

**Possible Causes**:
- Device firmware doesn't fully implement HTTP protocol
- Device returning custom error format instead of standard HTTP
- Device responding with plain text instead of HTTP headers

**Resolution**:
1. Check device firmware version - update if available
2. Verify device API documentation for correct endpoints
3. Test device API manually using curl or Postman
4. Contact device manufacturer if issue persists

### Timeout Errors
**Symptom**: Error mentions "timed out after 5 seconds"

**Possible Causes**:
- Device is offline or unreachable
- Network connectivity issues
- Device is overloaded and not responding quickly enough
- Firewall blocking communication

**Resolution**:
1. Verify device is powered on and network connected
2. Ping device IP address to check connectivity
3. Check firewall rules on agent and device
4. Verify device is not processing other heavy operations

### Connection Refused
**Symptom**: Error mentions "connection refused" or "no connection could be made"

**Possible Causes**:
- Device API service not running
- Incorrect port number in device configuration
- Device firewall blocking the port

**Resolution**:
1. Verify correct port number in device settings
2. Check device API service is running
3. Test connection from agent host: `telnet <device-ip> <port>`
4. Review device firewall settings

## Best Practices

1. **Regular Monitoring**: Check Agent Health Dashboard daily for error trends
2. **Device Documentation**: Keep device API documentation handy for troubleshooting
3. **Network Testing**: Periodically verify network connectivity to devices
4. **Firmware Updates**: Keep device firmware updated for bug fixes
5. **Error Patterns**: Look for patterns in errors (same device, same time of day)

## Technical Details

### HTTP Client Configuration
The agent now uses these settings for device communication:
- **Protocol**: HTTP/1.1 (explicit, with fallback to 1.0 if needed)
- **Connect Timeout**: 3 seconds
- **Request Timeout**: 5 seconds total
- **Connection Lifetime**: 5 minutes (prevents stale connections)

### Error Recording
All command execution results (success or failure) are recorded in Azure Table Storage:
- **Table**: CommandHistory
- **Partition Key**: TenantId
- **Retention**: Automatically pruned after 90 days (configurable)
- **Fields**: CommandId, DeviceId, Success, ErrorMessage, HttpStatusCode, ExecutionTimeMs

### Log Filtering
Use these structured log queries to find specific errors:

**In agent logs (systemd/journalctl):**
```bash
# All malformed HTTP errors
journalctl -u ProdControlAV.Agent | grep "Malformed HTTP response"

# Errors for specific device
journalctl -u ProdControlAV.Agent | grep "10.10.30.235:9993"

# All timeout errors
journalctl -u ProdControlAV.Agent | grep "timed out"
```

**In Application Insights (if configured):**
```kusto
traces
| where message contains "Malformed HTTP response"
| where customDimensions.DeviceIp == "10.10.30.235"
| order by timestamp desc
```

## Getting Help

If you continue to experience issues:
1. Check the Agent Health Dashboard for error details
2. Review agent logs using `journalctl -u ProdControlAV.Agent -f`
3. Verify device is accessible from agent host
4. Consult device manufacturer documentation
5. Open an issue on GitHub with error details and device model

---
For more information, see [DEPLOYMENT.md](DEPLOYMENT.md) and [AGENT_TESTING_GUIDE.md](AGENT_TESTING_GUIDE.md).
