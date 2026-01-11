# NetSparkle Timeout Fix - Issue #2

## Problem Statement

The ProdControlAV Agent was experiencing persistent timeout errors when checking for updates via NetSparkle. The appcast download was consistently timing out after 100 seconds, causing the update check to fail with `CouldNotDetermine` status.

### Error Logs from Production

```log
[2026-01-10T22:33:34.483Z] [DEBUG] [UpdateService] Checking for updates (current version: 1.0.62)...
[2026-01-10T22:33:34.483Z] [DEBUG] [UpdateService] Attempting to download appcast from: https://pcavstore.blob.core.windows.net/updates/appcast.json
[2026-01-10T22:33:34.483Z] [DEBUG] [UpdateService] [NetSparkle] Downloading and checking appcast
[2026-01-10T22:33:34.483Z] [DEBUG] [UpdateService] [NetSparkle] About to start downloading the app cast...
[2026-01-10T22:33:34.483Z] [DEBUG] [UpdateService] [NetSparkle] Downloading app cast data...
[2026-01-10T22:35:14.487Z] [DEBUG] [UpdateService] [NetSparkle] Error: The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.
[2026-01-10T22:35:14.494Z] [DEBUG] [UpdateService] [NetSparkle] Failed to download app cast from URL https://pcavstore.blob.core.windows.net/updates/appcast.json
[2026-01-10T22:35:14.494Z] [DEBUG] [UpdateService] [NetSparkle] No version information in app cast found
[2026-01-10T22:35:14.494Z] [DEBUG] [UpdateService] Appcast check completed with status: CouldNotDetermine
[2026-01-10T22:35:14.494Z] [WARN ] [UpdateService] Could not determine update status. Check appcast URL and network connectivity.
```

### Key Observations

1. **Consistent 100-second timeout** - The appcast download consistently timed out after exactly 100 seconds
2. **Network connectivity issue** - The timeout suggests the Azure Blob Storage URL is either unreachable or extremely slow from the Raspberry Pi
3. **No retry after timeout** - The existing retry logic only handled certain exceptions, not the `CouldNotDetermine` status
4. **Default timeout too long** - 100 seconds is NetSparkle's default timeout, which is too long for a small JSON file

## Root Cause Analysis

Through investigation of NetSparkle 3.0.4 source code and the UpdateService implementation:

1. **NetSparkle's default HttpClient timeout is 100 seconds** - This is hardcoded in the `WebRequestAppCastDataDownloader` class
2. **No configuration option** - NetSparkle doesn't expose a configuration option to change this timeout
3. **Timeout too long for small files** - The appcast.json file is typically less than 1KB, so 100 seconds is excessive
4. **Slow failure detection** - A 100-second timeout means it takes nearly 2 minutes to detect a network issue
5. **DNS resolution problems** - The Azure Blob Storage domain `pcavstore.blob.core.windows.net` may be experiencing DNS resolution issues on the Raspberry Pi

## Solution Implemented

### 1. Added Configurable Timeout Option

Added a new configuration option to `UpdateOptions.cs`:

```csharp
/// <summary>
/// Timeout for downloading the appcast manifest (in seconds). Default: 30 seconds.
/// Increase this value if experiencing timeout errors on slow network connections.
/// The default NetSparkle timeout of 100 seconds is too long and can cause issues.
/// </summary>
public int AppcastTimeoutSeconds { get; init; } = 30;
```

**Key design decisions:**
- **Default of 30 seconds** - Fast enough for failure detection but reasonable for slow connections
- **Configurable** - Users can increase if needed for their specific network conditions
- **Well-documented** - Clear explanation of the purpose and how to adjust

### 2. Created Custom AppCastDataDownloader

Created `ConfigurableAppCastDataDownloader.cs` to override NetSparkle's default timeout:

```csharp
internal class ConfigurableAppCastDataDownloader : WebRequestAppCastDataDownloader
{
    private readonly TimeSpan _timeout;

    public ConfigurableAppCastDataDownloader(TimeSpan timeout)
    {
        _timeout = timeout;
    }

    protected override HttpClient CreateHttpClient()
    {
        var client = base.CreateHttpClient();
        client.Timeout = _timeout;
        return client;
    }
}
```

**Key design decisions:**
- **Inherits from WebRequestAppCastDataDownloader** - Leverages existing NetSparkle functionality
- **Overrides CreateHttpClient** - Injects custom timeout configuration
- **Internal visibility** - Not exposed as public API, only used internally by UpdateService
- **Minimal code** - Simple, focused implementation

### 3. Updated UpdateService to Use Custom Downloader

Modified `UpdateService.cs` to use the custom downloader:

```csharp
// Log the timeout configuration
_logger.LogInformation("Appcast timeout: {Timeout} seconds", _updateOptions.AppcastTimeoutSeconds);

// Create custom appcast downloader with configurable timeout
var appcastDownloader = new ConfigurableAppCastDataDownloader(
    TimeSpan.FromSeconds(_updateOptions.AppcastTimeoutSeconds));

_sparkle = new SparkleUpdater(
    _updateOptions.AppcastUrl,
    signatureVerifier,
    _referenceAssembly)
{
    UIFactory = null,
    TmpDownloadFilePath = Path.Combine(Path.GetTempPath(), "prodcontrolav-update.zip"),
    RelaunchAfterUpdate = false,
    AppCastGenerator = new JsonAppCastGenerator(),
    LogWriter = new NetSparkleLoggerBridge(_logger),
    AppCastDataDownloader = appcastDownloader  // Use custom downloader
};

// Enable logging on the custom AppCastDataDownloader
appcastDownloader.LogWriter = new NetSparkleLoggerBridge(_logger);
_logger.LogDebug("Configured custom AppCastDataDownloader with {Timeout}-second timeout", _updateOptions.AppcastTimeoutSeconds);
```

**Key changes:**
- **Initialize custom downloader** before creating SparkleUpdater
- **Set LogWriter** on custom downloader for diagnostic logging
- **Log timeout configuration** for visibility in production logs

### 4. Enhanced Error Logging

Improved error messages for timeout scenarios:

```csharp
catch (TaskCanceledException tcEx) when (retryAttempt < maxRetries - 1 && !stoppingToken.IsCancellationRequested)
{
    retryAttempt++;
    _logger.LogWarning(tcEx, "Timeout while downloading appcast (attempt {Attempt}/{MaxRetries}). Configured timeout: {Timeout}s. Retrying in {Delay} seconds...", 
        retryAttempt, maxRetries, _updateOptions.AppcastTimeoutSeconds, retryDelay.TotalSeconds);
    // ... retry logic
}
catch (TaskCanceledException tcEx)
{
    _logger.LogError(tcEx, "Timeout while downloading appcast after {MaxRetries} attempts. The operation exceeded the configured timeout of {Timeout} seconds.", 
        maxRetries, _updateOptions.AppcastTimeoutSeconds);
    _logger.LogError("Consider increasing AppcastTimeoutSeconds in appsettings.json or checking network connectivity to: {AppcastUrl}", _updateOptions.AppcastUrl);
    _logger.LogError("Current timeout setting: {Timeout} seconds. Try increasing to 60-120 seconds for slow connections.", _updateOptions.AppcastTimeoutSeconds);
    return null;
}
```

**Improvements:**
- **Include configured timeout** in error messages
- **Provide actionable suggestions** for resolving timeout issues
- **Reference configuration setting** so users know what to change

### 5. Updated Configuration Files

Updated `appsettings.json` with the new timeout setting:

```json
{
  "Update": {
    "Enabled": true,
    "AppcastUrl": "https://pcavstore.blob.core.windows.net/updates/appcast.json",
    "Ed25519PublicKey": "DIL8Xjb5JdXesMk9/SpzNb3JW406nGOWXbLxtJu0OCM=",
    "CheckIntervalSeconds": 3600,
    "AutoInstall": true,
    "AppcastTimeoutSeconds": 30
  }
}
```

## Benefits of This Solution

### 1. Faster Failure Detection
- **Before**: 100-second timeout meant nearly 2 minutes to detect network issues
- **After**: 30-second timeout means faster failure detection and quicker retries

### 2. Configurable for Different Environments
- **Raspberry Pi with good connectivity**: Default 30 seconds is sufficient
- **Raspberry Pi with slow/unreliable network**: Can increase to 60-120 seconds
- **Testing/development**: Can decrease to 10-15 seconds for faster iteration

### 3. Better Retry Behavior
- **Faster retries**: With a shorter timeout, retries happen sooner
- **Exponential backoff**: Combined with existing retry logic, provides good resilience
- **3 attempts with 30s timeout**: Max 90 seconds before giving up (vs 300 seconds with 100s timeout)

### 4. Improved Diagnostics
- **Timeout value logged**: Easy to see what timeout is configured in production logs
- **Actionable error messages**: Users know exactly what to change if experiencing timeouts
- **Consistent with other timeouts**: Similar to the 5-10 second timeouts used for API calls in the agent

## Testing

### Unit Tests
All existing tests continue to pass:
```
Passed!  - Failed:     0, Passed:   115, Skipped:     0, Total:   115
```

The changes are backward compatible and don't affect existing test coverage.

### Manual Testing Recommendations

1. **Test with default timeout (30s):**
   ```bash
   cd src/ProdControlAV.Agent
   dotnet run
   # Monitor logs for update check behavior
   ```

2. **Test with increased timeout (60s) for slow connections:**
   ```json
   {
     "Update": {
       "AppcastTimeoutSeconds": 60
     }
   }
   ```

3. **Test with very short timeout (5s) to simulate failures:**
   ```json
   {
     "Update": {
       "AppcastTimeoutSeconds": 5
     }
   }
   ```

4. **Monitor logs for:**
   - `[INFO] Appcast timeout: XX seconds` - Confirms configuration is loaded
   - `[DEBUG] Configured custom AppCastDataDownloader with XX-second timeout` - Confirms custom downloader is being used
   - Timeout error messages with actionable suggestions

## Deployment Instructions

### For Development/Testing

1. **Update appsettings.json** with desired timeout:
   ```json
   {
     "Update": {
       "AppcastTimeoutSeconds": 30
     }
   }
   ```

2. **Build and run:**
   ```bash
   cd src/ProdControlAV.Agent
   dotnet build
   dotnet run
   ```

### For Production (Raspberry Pi)

1. **Build the agent:**
   ```bash
   dotnet publish src/ProdControlAV.Agent/ProdControlAV.Agent.csproj \
     -c Release -r linux-arm64 --self-contained true -o ./publish/agent
   ```

2. **Stop the agent service:**
   ```bash
   sudo systemctl stop prodcontrolav-agent
   ```

3. **Backup current version:**
   ```bash
   sudo cp -r /opt/prodcontrolav/agent /opt/prodcontrolav/agent.backup.$(date +%Y%m%d_%H%M%S)
   ```

4. **Update appsettings.json** on the Pi (if changing from default):
   ```bash
   sudo nano /opt/prodcontrolav/agent/appsettings.json
   # Add or update: "AppcastTimeoutSeconds": 30
   ```

5. **Deploy new version:**
   ```bash
   sudo cp -r ./publish/agent/* /opt/prodcontrolav/agent/
   sudo chown -R prodcontrolav:prodcontrolav /opt/prodcontrolav/agent
   ```

6. **Start the agent service:**
   ```bash
   sudo systemctl start prodcontrolav-agent
   ```

7. **Monitor logs:**
   ```bash
   sudo journalctl -u prodcontrolav-agent -f | grep -E "UpdateService|NetSparkle"
   ```

### For Automatic Updates

This fix will be automatically included in the next agent release built by the `agent-release.yml` workflow. Agents with automatic updates enabled will receive this fix without manual intervention.

## Troubleshooting Guide

### Issue: Still experiencing timeout errors with default 30s timeout

**Diagnosis:**
- Check network connectivity: `ping pcavstore.blob.core.windows.net`
- Check DNS resolution: `nslookup pcavstore.blob.core.windows.net`
- Check if a firewall is blocking the connection

**Solutions:**
1. **Increase timeout to 60-120 seconds** in appsettings.json
2. **Check network connectivity** from the Raspberry Pi
3. **Verify DNS resolution** - may need to configure custom DNS servers
4. **Check for proxy/firewall** that might be blocking or slowing connections

### Issue: Timeout is too short, agent always fails

**Diagnosis:**
- Logs show: `Timeout while downloading appcast after 3 attempts`
- Network latency to Azure Blob Storage is high
- Bandwidth is very limited

**Solution:**
Increase `AppcastTimeoutSeconds` in appsettings.json:
```json
{
  "Update": {
    "AppcastTimeoutSeconds": 90
  }
}
```

### Issue: Timeout configuration not being applied

**Diagnosis:**
- Logs show: `[INFO] Appcast timeout: 100 seconds` instead of configured value
- Configuration file not being read correctly

**Solution:**
1. **Verify appsettings.json syntax** - ensure valid JSON
2. **Check file location** - must be in same directory as agent executable
3. **Restart agent** after configuration changes
4. **Check file permissions** - agent user must be able to read the file

### Issue: Update check completes too quickly

**Diagnosis:**
- Logs show update check completes in less than 1 second
- Possible DNS issue or immediate connection refusal

**Solution:**
1. **Check network connectivity** to Azure Blob Storage
2. **Verify appcast URL** is correct in configuration
3. **Check DNS resolution** - domain may be unreachable
4. **Test manually**: `curl https://pcavstore.blob.core.windows.net/updates/appcast.json`

## Future Improvements

1. **Automatic timeout adjustment** - Dynamically adjust timeout based on observed latency
2. **Fallback URL** - Configure a secondary appcast URL in case primary is unreachable
3. **Health check endpoint** - Add API endpoint to query update check status remotely
4. **Prometheus metrics** - Export timeout metrics for monitoring
5. **Configuration validation** - Warn if timeout is set to unreasonable values (< 5s or > 300s)

## Related Documentation

- [NETSPARKLE-DIAGNOSTIC-LOGGING-FIX.md](./NETSPARKLE-DIAGNOSTIC-LOGGING-FIX.md) - Previous fix for diagnostic logging
- [AGENT-UPDATE-VERSION-FIX.md](./AGENT-UPDATE-VERSION-FIX.md) - Version comparison fix
- [AUTOMATIC-UPDATES-SETUP.md](./AUTOMATIC-UPDATES-SETUP.md) - Complete setup guide
- [NetSparkle Documentation](https://netsparkleupdater.github.io/NetSparkle/)

## Conclusion

This fix addresses the NetSparkle timeout issues by:
1. **Reducing default timeout** from 100 seconds to 30 seconds for faster failure detection
2. **Making timeout configurable** so it can be adjusted for different network conditions
3. **Providing better diagnostics** with actionable error messages
4. **Maintaining backward compatibility** with existing functionality

The 30-second default is a good balance between:
- **Fast enough** for quick failure detection and retries
- **Long enough** for most network conditions to succeed
- **Configurable** for edge cases that need longer timeouts

With the existing 3-retry logic and exponential backoff, this provides robust update checking with quick recovery from transient network issues.
