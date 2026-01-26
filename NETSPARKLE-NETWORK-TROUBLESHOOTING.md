# NetSparkle Network Connectivity Troubleshooting Guide

## Issue: HttpClient Connection Timeouts to Azure Blob Storage

### Symptoms
```
[DEBUG] HTTP error after 133.58 seconds: Connection timed out (pcavstore.blob.core.windows.net:443)
[DEBUG] Inner exception: Connection timed out
```

The agent's HttpClient **cannot establish a TCP connection** to Azure Blob Storage, but `curl` works successfully.

## Root Cause

This is a **known issue with .NET HttpClient on Raspberry Pi** when using certain OpenSSL/libssl versions. The problem is NOT a network/firewall issue if curl works - it's a .NET runtime compatibility issue.

### Technical Details

1. **.NET HttpClient** uses the system's OpenSSL library for HTTPS connections
2. **Raspberry Pi OS** may have incompatible or outdated OpenSSL/libssl versions
3. **curl** uses its own built-in SSL/TLS implementation (or libcurl), which is why it works
4. The "Connection timed out" error occurs during the SSL/TLS handshake, not the TCP connection

## Solution Implemented

### Automatic Fallback to curl

The agent now automatically falls back to using `curl` when HttpClient fails:

1. **Primary Method**: HttpClient with optimized configuration
2. **Fallback Method**: curl command-line tool (if HttpClient fails)
3. **Detailed Diagnostics**: Logs show exactly what failed and what succeeded

### Expected Behavior After Fix

**Scenario 1: HttpClient Works (Ideal)**
```
[DEBUG] Starting appcast download (async) from: https://...
[DEBUG] Connection established in 2.5 seconds
[DEBUG] Successfully downloaded appcast (1234 bytes) in 3.2 seconds
```

**Scenario 2: HttpClient Fails, curl Succeeds (Workaround)**
```
[DEBUG] HTTP error after 133.58 seconds: Connection timed out
[DEBUG] FALLBACK: Attempting download with curl as HttpClient failed...
[DEBUG] SUCCESS: curl downloaded appcast (1234 bytes)
[DEBUG] RECOMMENDATION: HttpClient is failing but curl works - this indicates a .NET/OpenSSL compatibility issue
[DEBUG] PERMANENT FIX: Update to latest .NET runtime or install libssl1.1 on Raspberry Pi
```

**Scenario 3: Both Fail (Actual Network Issue)**
```
[DEBUG] HTTP error after 133.58 seconds: Connection timed out
[DEBUG] FALLBACK: Attempting download with curl...
[DEBUG] curl also failed with exit code 28: Connection timed out
[DEBUG] CRITICAL: Both HttpClient and curl are failing - this is a fundamental network connectivity issue
[DEBUG] ACTION REQUIRED: Verify network connectivity with: ping pcavstore.blob.core.windows.net
```

## Permanent Fixes

### Option 1: Update .NET Runtime (Recommended)

```bash
# Check current .NET version
dotnet --version

# If older than 8.0.119, update to latest
wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
chmod +x dotnet-install.sh
sudo ./dotnet-install.sh --channel 8.0 --install-dir /usr/share/dotnet
```

### Option 2: Install Compatible OpenSSL Version

```bash
# Install libssl1.1 (if not present)
sudo apt-get update
sudo apt-get install libssl1.1

# Verify installation
ls -la /usr/lib/arm-linux-gnueabihf/libssl.so.*
```

### Option 3: Update Raspberry Pi OS

```bash
# Update all packages (may require reboot)
sudo apt-get update
sudo apt-get upgrade -y
sudo apt-get dist-upgrade -y
```

### Option 4: Use curl Permanently

The fallback is now automatic, but if you want to force curl usage for better reliability, you can modify the code to always use curl instead of HttpClient.

## Verification

After applying any fix, check the logs for:

```
[DEBUG] Connection established in X.X seconds
[DEBUG] Successfully downloaded appcast (1234 bytes)
```

If you see:
```
[DEBUG] SUCCESS: curl downloaded appcast
```

Then the fallback is working but you should still apply a permanent fix.

## Additional Network Checks

If both HttpClient and curl fail, verify:

### 1. DNS Resolution
```bash
nslookup pcavstore.blob.core.windows.net
# Should resolve to an Azure IP address
```

### 2. Network Connectivity
```bash
ping -c 4 pcavstore.blob.core.windows.net
# Should get responses
```

### 3. HTTPS Port Access
```bash
curl -v https://pcavstore.blob.core.windows.net/updates/appcast.json
# Should return JSON data
```

### 4. Certificate Issues
```bash
curl -v --insecure https://pcavstore.blob.core.windows.net/updates/appcast.json
# If this works but normal curl doesn't, it's a certificate issue
```

### 5. Check System Time
```bash
date
# System time must be accurate for SSL/TLS to work
# If wrong, update with: sudo ntpdate -u time.nist.gov
```

## Common Causes

| Error | Likely Cause | Solution |
|-------|--------------|----------|
| Connection timed out (HttpClient only) | .NET/OpenSSL incompatibility | Install libssl1.1 or update .NET |
| Connection timed out (both) | Firewall blocking port 443 | Configure firewall rules |
| Name or service not known | DNS failure | Check /etc/resolv.conf |
| SSL certificate problem | Wrong system time or missing CA certs | Update time and ca-certificates |
| Connection refused | Azure IP changed or blocked | Update DNS cache, check routing |

## Related Files

- `src/ProdControlAV.Agent/Services/ConfigurableAppCastDataDownloader.cs` - Implements fallback logic
- `src/ProdControlAV.Agent/Services/UpdateOptions.cs` - Timeout configuration
- `src/ProdControlAV.Agent/appsettings.json` - Configure `AppcastTimeoutSeconds`

## References

- [.NET HttpClient Known Issues on Linux](https://github.com/dotnet/runtime/issues/30667)
- [Raspberry Pi SSL/TLS with .NET](https://github.com/dotnet/runtime/issues/42897)
- [NetSparkle Documentation](https://github.com/NetSparkleUpdater/NetSparkle)
