using System.Reflection;
using System.IO.Compression;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.SignatureVerifiers;
using NetSparkleUpdater.AppCastHandlers;
using NetSparkleUpdater.Interfaces;

namespace ProdControlAV.Agent.Services;

/// <summary>
/// Logging bridge to capture NetSparkle's internal diagnostic messages
/// and forward them to the application's ILogger instance.
/// </summary>
internal class NetSparkleLoggerBridge : NetSparkleUpdater.Interfaces.ILogger
{
    private readonly Microsoft.Extensions.Logging.ILogger<UpdateService> _logger;

    public NetSparkleLoggerBridge(Microsoft.Extensions.Logging.ILogger<UpdateService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void PrintMessage(string message, params object[]? parameters)
    {
        try
        {
            // Format the message with parameters if provided
            var formattedMessage = parameters != null && parameters.Length > 0
                ? string.Format(message, parameters)
                : message;
            
            // Log at Debug level by default (NetSparkle doesn't provide log level in this interface)
            _logger.LogDebug("[NetSparkle] {Message}", formattedMessage);
        }
        catch (Exception ex)
        {
            // Fallback if formatting fails
            _logger.LogDebug(ex, "[NetSparkle] {RawMessage}", message);
        }
    }
}

/// <summary>
/// Background service that checks for and applies updates using NetSparkle.
/// Runs in headless mode for automatic updates on Raspberry Pi deployment.
/// Supports automatic and manual update triggering with backup and rollback capabilities.
/// </summary>
public sealed class UpdateService : BackgroundService
{
    private readonly ILogger<UpdateService> _logger;
    private readonly UpdateOptions _updateOptions;
    private readonly string _currentVersion;
    private readonly string _currentVersionRaw;
    private readonly string _agentDirectory;
    private readonly string _backupBaseDirectory;
    private readonly string _referenceAssembly;
    private SparkleUpdater? _sparkle;
    private readonly SemaphoreSlim _updateLock = new SemaphoreSlim(1, 1);

    public UpdateService(
        ILogger<UpdateService> logger,
        IOptions<UpdateOptions> updateOptions)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _updateOptions = updateOptions?.Value ?? throw new ArgumentNullException(nameof(updateOptions));
        
        // Get the current agent version from assembly
        var assembly = Assembly.GetExecutingAssembly();
        _currentVersionRaw = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion 
            ?? assembly.GetName().Version?.ToString() 
            ?? "0.0.0";
        
        // Strip build metadata (+<hash>) for NetSparkle version comparison
        // SemVer format: major.minor.patch[-prerelease][+buildmetadata]
        // NetSparkle needs the version without build metadata for proper comparison
        _currentVersion = StripBuildMetadata(_currentVersionRaw);
            
        // Determine agent directory (where the agent is installed)
        _agentDirectory = Path.GetDirectoryName(assembly.Location) ?? "/opt/prodcontrolav/agent";
        
        // Store reference assembly path for NetSparkle configuration
        _referenceAssembly = assembly.Location;
        
        // Backup directory
        _backupBaseDirectory = "/opt/prodcontrolav";
    }
    
    /// <summary>
    /// Strips build metadata from a semantic version string following SemVer 2.0.0 specification.
    /// Build metadata is identified by a plus sign (+) and should be ignored for version precedence.
    /// </summary>
    /// <param name="version">The version string to process. Can be null or empty.</param>
    /// <returns>
    /// The version string with build metadata removed. Returns "0.0.0" if input is null or whitespace.
    /// Examples:
    /// - "1.0.51+8754e5a" -> "1.0.51"
    /// - "1.0.0-beta+build" -> "1.0.0-beta" (preserves pre-release tag)
    /// - "1.0.0" -> "1.0.0" (no change if no metadata)
    /// - null or "" -> "0.0.0"
    /// </returns>
    internal static string StripBuildMetadata(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return "0.0.0";
        }
        
        var plusIndex = version.IndexOf('+');
        return plusIndex >= 0 ? version.Substring(0, plusIndex) : version;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Check if updates are enabled
        if (!_updateOptions.Enabled)
        {
            _logger.LogInformation("Automatic updates are disabled in configuration");
            return;
        }

        // Validate configuration
        if (string.IsNullOrWhiteSpace(_updateOptions.AppcastUrl))
        {
            _logger.LogWarning("Update appcast URL is not configured. Automatic updates disabled.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_updateOptions.Ed25519PublicKey))
        {
            _logger.LogWarning("Update Ed25519 public key is not configured. Automatic updates disabled for security.");
            return;
        }

        try
        {
            // Initialize NetSparkle with Ed25519 signature verification
            _logger.LogInformation("Initializing NetSparkle update system...");
            _logger.LogInformation("Current agent version (raw): {CurrentVersionRaw}", _currentVersionRaw);
            _logger.LogInformation("Current agent version (for comparison): {CurrentVersion}", _currentVersion);
            _logger.LogInformation("Reference assembly: {ReferenceAssembly}", _referenceAssembly);
            _logger.LogInformation("Agent directory: {AgentDirectory}", _agentDirectory);
            _logger.LogInformation("Backup directory: {BackupDirectory}", _backupBaseDirectory);
            _logger.LogInformation("Appcast URL: {AppcastUrl}", _updateOptions.AppcastUrl);
            _logger.LogInformation("Check interval: {Interval} seconds", _updateOptions.CheckIntervalSeconds);
            _logger.LogInformation("Auto-install: {AutoInstall}", _updateOptions.AutoInstall);
            _logger.LogInformation("Appcast timeout: {Timeout} seconds", _updateOptions.AppcastTimeoutSeconds);

            var signatureVerifier = new Ed25519Checker(SecurityMode.Strict, _updateOptions.Ed25519PublicKey);
            
            // Create custom appcast downloader with configurable timeout
            var appcastDownloader = new ConfigurableAppCastDataDownloader(
                TimeSpan.FromSeconds(_updateOptions.AppcastTimeoutSeconds));

            _sparkle = new SparkleUpdater(
                _updateOptions.AppcastUrl,
                signatureVerifier,
                _referenceAssembly)  // Pass reference assembly path for configuration storage
            {
                // Headless mode - no UI
                UIFactory = null,
                TmpDownloadFilePath = Path.Combine(Path.GetTempPath(), "prodcontrolav-update.zip"),
                RelaunchAfterUpdate = false,  // We handle restart via Environment.Exit(0) and systemd
                // Configure JSON appcast generator (NetSparkle defaults to XML)
                AppCastGenerator = new JsonAppCastGenerator(),
                // Enable NetSparkle internal diagnostic logging
                LogWriter = new NetSparkleLoggerBridge(_logger),
                // Use custom downloader with configurable timeout
                AppCastDataDownloader = appcastDownloader
            };
            
            // Enable logging on the custom AppCastDataDownloader
            appcastDownloader.LogWriter = new NetSparkleLoggerBridge(_logger);
            _logger.LogDebug("Configured custom AppCastDataDownloader with {Timeout}-second timeout", _updateOptions.AppcastTimeoutSeconds);
            
            // NetSparkle reads the version from the reference assembly using AssemblyInformationalVersion.
            // Per SemVer 2.0.0 spec, build metadata (the +hash part) should be ignored for version precedence.
            // NetSparkle uses the Chaos.NaCl.Ed25519 library which handles SemVer comparisons.
            // The cleaned version is logged above for diagnostic purposes.

            _logger.LogInformation("NetSparkle update system initialized successfully");
            _logger.LogInformation("Note: File logging for UpdateService is active in logs/updateService/ folder");

            // Run the update check loop
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Check for manual update trigger signal
                    var updateSignalFile = Path.Combine(Path.GetTempPath(), "prodcontrolav-update-trigger");
                    var manualTrigger = File.Exists(updateSignalFile);
                    
                    if (manualTrigger)
                    {
                        _logger.LogInformation("Manual update trigger detected, checking for updates immediately...");
                        try
                        {
                            File.Delete(updateSignalFile);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to delete update trigger file");
                        }
                    }
                    else
                    {
                        _logger.LogDebug("Checking for updates (current version: {CurrentVersion})...", _currentVersion);
                    }
                    
                    // Pre-check: Verify the appcast URL's domain is resolvable before attempting download
                    // This provides fast failure with clear error messages for DNS/network issues
                    _logger.LogDebug("Attempting to download appcast from: {AppcastUrl}", _updateOptions.AppcastUrl);
                    
                    bool isDnsAccessible = await VerifyAppcastUrlAccessibilityAsync();
                    if (!isDnsAccessible)
                    {
                        _logger.LogError("Skipping update check due to DNS resolution failure. Will retry on next interval.");
                        _logger.LogError("This is typically caused by incorrect AppcastUrl configuration or network issues.");
                        continue;
                    }
                    
                    // Check for updates with detailed logging and retry logic
                    UpdateInfo? updateInfo = await CheckForUpdatesWithRetryAsync(stoppingToken);
                    
                    // If all retries failed, skip to next iteration
                    if (updateInfo == null)
                    {
                        // Error already logged in CheckForUpdatesWithRetryAsync
                        continue;
                    }
                    
                    if (updateInfo.Status == UpdateStatus.UpdateAvailable)
                    {
                        var latestVersion = updateInfo.Updates?.FirstOrDefault()?.Version ?? "unknown";
                        _logger.LogInformation(
                            "Update available: Version {LatestVersion} (current: {CurrentVersion})",
                            latestVersion, _currentVersion);

                        if (_updateOptions.AutoInstall || manualTrigger)
                        {
                            if (manualTrigger)
                            {
                                _logger.LogInformation("Manual update trigger - applying update immediately...");
                            }
                            else
                            {
                                _logger.LogInformation("Auto-install is enabled. Applying update...");
                            }
                            await ApplyUpdateAsync(updateInfo, stoppingToken);
                        }
                        else
                        {
                            _logger.LogInformation("Auto-install is disabled. Manual update trigger required from dashboard.");
                        }
                    }
                    else if (updateInfo.Status == UpdateStatus.UpdateNotAvailable)
                    {
                        if (manualTrigger)
                        {
                            _logger.LogInformation("Manual update triggered but no updates available. Current version {CurrentVersion} is up to date.", _currentVersion);
                        }
                        else
                        {
                            _logger.LogDebug("No updates available. Current version {CurrentVersion} is up to date.", _currentVersion);
                        }
                    }
                    else if (updateInfo.Status == UpdateStatus.CouldNotDetermine)
                    {
                        _logger.LogWarning("Could not determine update status. Check appcast URL and network connectivity.");
                        _logger.LogWarning("Appcast URL being used: {AppcastUrl}", _updateOptions.AppcastUrl);
                        _logger.LogWarning("Current version: {CurrentVersion} (raw: {CurrentVersionRaw})", _currentVersion, _currentVersionRaw);
                        _logger.LogWarning("Ensure the URL is accessible and the appcast.json file exists at that location.");
                        _logger.LogWarning("Common causes: network issues, invalid JSON format, signature verification failure, or incorrect appcast structure.");
                        _logger.LogWarning("Check the [NetSparkle] debug logs above for detailed diagnostic information.");
                    }
                    else
                    {
                        _logger.LogWarning("Unknown update status: {Status}", updateInfo.Status);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error checking for updates");
                }

                // Wait for the next check interval
                await Task.Delay(TimeSpan.FromSeconds(_updateOptions.CheckIntervalSeconds), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Update service is shutting down");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize update system");
        }
    }

    /// <summary>
    /// Pre-checks if the appcast URL's domain is resolvable via DNS.
    /// This helps detect network/DNS issues early before waiting for HTTP timeout.
    /// </summary>
    /// <returns>True if DNS resolution succeeds, false otherwise</returns>
    private async Task<bool> VerifyAppcastUrlAccessibilityAsync()
    {
        const int DnsTimeoutSeconds = 5;
        
        try
        {
            var uri = new Uri(_updateOptions.AppcastUrl);
            var hostname = uri.Host;
            
            _logger.LogDebug("Verifying DNS resolution for appcast host: {Hostname}", hostname);
            
            // Attempt DNS resolution with a short timeout
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(DnsTimeoutSeconds));
            var addresses = await Dns.GetHostAddressesAsync(hostname, cts.Token);
            
            if (addresses == null || addresses.Length == 0)
            {
                _logger.LogError("DNS resolution failed for {Hostname}. No IP addresses returned.", hostname);
                _logger.LogError("The configured AppcastUrl uses a domain name that does not exist or cannot be resolved.");
                _logger.LogError("Common causes:");
                
                // Extract storage account name safely
                var hostParts = hostname.Split('.');
                if (hostParts.Length > 0 && hostname.Contains("blob.core.windows.net"))
                {
                    _logger.LogError("  1. The Azure Storage account '{StorageAccount}' does not exist", hostParts[0]);
                }
                else
                {
                    _logger.LogError("  1. The domain '{Domain}' does not exist or is unreachable", hostname);
                }
                
                _logger.LogError("  2. The domain name is misspelled in appsettings.json");
                _logger.LogError("  3. The storage account has been deleted or renamed");
                _logger.LogError("Current AppcastUrl: {AppcastUrl}", _updateOptions.AppcastUrl);
                _logger.LogError("Verify the correct storage account name in Azure Portal and update appsettings.json");
                return false;
            }
            
            _logger.LogDebug("DNS resolution successful for {Hostname}: {IpAddresses}", 
                hostname, string.Join(", ", addresses.Select(a => a.ToString())));
            return true;
        }
        catch (TaskCanceledException)
        {
            var uri = new Uri(_updateOptions.AppcastUrl);
            _logger.LogError("DNS resolution timeout for {Hostname}. The domain could not be resolved within {Timeout} seconds.", 
                uri.Host, DnsTimeoutSeconds);
            _logger.LogError("This indicates a network or DNS configuration issue. Check:");
            _logger.LogError("  1. Network connectivity: Can the agent reach the internet?");
            _logger.LogError("  2. DNS server configuration: Is DNS properly configured?");
            _logger.LogError("  3. Firewall rules: Is DNS traffic (UDP port 53) allowed?");
            _logger.LogError("  4. Azure Blob Storage domain: Does '{Hostname}' exist?", uri.Host);
            _logger.LogError("Manual test: ping {Hostname} or nslookup {Hostname}", uri.Host, uri.Host);
            return false;
        }
        catch (SocketException sockEx)
        {
            var uri = new Uri(_updateOptions.AppcastUrl);
            _logger.LogError(sockEx, "DNS resolution failed with socket error for {Hostname}", uri.Host);
            _logger.LogError("Socket error code: {ErrorCode}. This typically indicates DNS server is unreachable or misconfigured.", sockEx.ErrorCode);
            _logger.LogError("Check network connectivity and DNS server configuration on this device.");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during DNS pre-check for appcast URL: {AppcastUrl}", _updateOptions.AppcastUrl);
            return false;
        }
    }

    /// <summary>
    /// Checks for updates with retry logic and exponential backoff
    /// </summary>
    /// <returns>UpdateInfo if successful, null if all retries failed</returns>
    private async Task<UpdateInfo?> CheckForUpdatesWithRetryAsync(CancellationToken stoppingToken)
    {
        const int maxRetries = 3;
        const int maxDelaySeconds = 60; // Cap retry delay at 60 seconds
        var retryDelay = TimeSpan.FromSeconds(5);
        var retryAttempt = 0;
        
        while (retryAttempt < maxRetries)
        {
            try
            {
                var updateInfo = await _sparkle.CheckForUpdatesQuietly();
                _logger.LogDebug("Appcast check completed with status: {Status}", updateInfo.Status);
                return updateInfo;
            }
            catch (System.Net.WebException webEx) when (retryAttempt < maxRetries - 1)
            {
                retryAttempt++;
                _logger.LogWarning(webEx, "Network error while downloading appcast (attempt {Attempt}/{MaxRetries}). Status: {Status}. Retrying in {Delay} seconds...", 
                    retryAttempt, maxRetries, webEx.Status, retryDelay.TotalSeconds);
                await Task.Delay(retryDelay, stoppingToken);
                retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, maxDelaySeconds)); // Exponential backoff with cap
                continue;
            }
            catch (System.Net.WebException webEx)
            {
                _logger.LogError(webEx, "Network error while downloading appcast after {MaxRetries} attempts. Status: {Status}", maxRetries, webEx.Status);
                _logger.LogError("This may be caused by: slow network connection, timeout (default 100s), DNS issues, or firewall blocking access");
                return null;
            }
            catch (TaskCanceledException tcEx) when (retryAttempt < maxRetries - 1 && !stoppingToken.IsCancellationRequested)
            {
                retryAttempt++;
                _logger.LogWarning(tcEx, "Timeout while downloading appcast (attempt {Attempt}/{MaxRetries}). Configured timeout: {Timeout}s. Retrying in {Delay} seconds...", 
                    retryAttempt, maxRetries, _updateOptions.AppcastTimeoutSeconds, retryDelay.TotalSeconds);
                await Task.Delay(retryDelay, stoppingToken);
                retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, maxDelaySeconds)); // Exponential backoff with cap
                continue;
            }
            catch (TaskCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Service is shutting down - re-throw
                throw;
            }
            catch (TaskCanceledException tcEx)
            {
                _logger.LogError(tcEx, "Timeout while downloading appcast after {MaxRetries} attempts. The operation exceeded the configured timeout of {Timeout} seconds.", 
                    maxRetries, _updateOptions.AppcastTimeoutSeconds);
                _logger.LogError("Consider increasing AppcastTimeoutSeconds in appsettings.json or checking network connectivity to: {AppcastUrl}", _updateOptions.AppcastUrl);
                _logger.LogError("Current timeout setting: {Timeout} seconds. Try increasing to 60-120 seconds for slow connections.", _updateOptions.AppcastTimeoutSeconds);
                return null;
            }
            catch (HttpRequestException httpEx) when (retryAttempt < maxRetries - 1)
            {
                retryAttempt++;
                _logger.LogWarning(httpEx, "HTTP error while downloading appcast (attempt {Attempt}/{MaxRetries}). Retrying in {Delay} seconds...", 
                    retryAttempt, maxRetries, retryDelay.TotalSeconds);
                await Task.Delay(retryDelay, stoppingToken);
                retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, maxDelaySeconds)); // Exponential backoff with cap
                continue;
            }
            catch (HttpRequestException httpEx)
            {
                _logger.LogError(httpEx, "HTTP error while downloading appcast after {MaxRetries} attempts", maxRetries);
                return null;
            }
        }
        
        _logger.LogError("Failed to check for updates after {MaxRetries} attempts. Will retry in next check interval.", maxRetries);
        return null;
    }

    /// <summary>
    /// Applies an update by downloading, backing up current version, extracting new version, and restarting.
    /// </summary>
    private async Task ApplyUpdateAsync(UpdateInfo updateInfo, CancellationToken stoppingToken)
    {
        await _updateLock.WaitAsync(stoppingToken);
        
        try
        {
            var updateItem = updateInfo.Updates?.FirstOrDefault();
            if (updateItem == null)
            {
                _logger.LogWarning("No update item available to apply");
                return;
            }

            var version = updateItem.Version;
            var downloadUrl = updateItem.DownloadLink;
            
            _logger.LogInformation("Starting update process for version {Version}", version);
            _logger.LogInformation("Download URL: {DownloadUrl}", downloadUrl);

            // Step 1: Create backup of current version
            var backupPath = CreateBackup();
            if (string.IsNullOrEmpty(backupPath))
            {
                _logger.LogError("Failed to create backup. Aborting update.");
                return;
            }

            try
            {
                // Step 2: Download the update
                var downloadPath = Path.Combine(Path.GetTempPath(), $"prodcontrolav-update-{version}.zip");
                _logger.LogInformation("Downloading update to: {DownloadPath}", downloadPath);
                
                using (var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
                {
                    var response = await httpClient.GetAsync(downloadUrl, stoppingToken);
                    response.EnsureSuccessStatusCode();
                    
                    await using var fileStream = File.Create(downloadPath);
                    await response.Content.CopyToAsync(fileStream, stoppingToken);
                }
                
                _logger.LogInformation("Update downloaded successfully");

                // Step 3: Verify signature (NetSparkle already verified, but double-check file exists)
                if (!File.Exists(downloadPath))
                {
                    throw new FileNotFoundException("Downloaded update file not found", downloadPath);
                }

                // Step 4: Extract update to agent directory
                _logger.LogInformation("Extracting update to agent directory: {AgentDirectory}", _agentDirectory);
                
                // Extract to temporary directory first to ensure extraction succeeds
                var extractTempPath = Path.Combine(Path.GetTempPath(), $"prodcontrolav-extract-{version}");
                if (Directory.Exists(extractTempPath))
                {
                    Directory.Delete(extractTempPath, true);
                }
                Directory.CreateDirectory(extractTempPath);
                
                ZipFile.ExtractToDirectory(downloadPath, extractTempPath, overwriteFiles: true);
                _logger.LogInformation("Update extracted to temporary directory: {TempPath}", extractTempPath);

                // Step 5: Copy extracted files to agent directory (overwrite)
                _logger.LogInformation("Copying files from temporary directory to agent directory");
                CopyDirectory(extractTempPath, _agentDirectory, overwrite: true);
                
                _logger.LogInformation("Update files copied successfully");

                // Step 6: Clean up temporary files
                try
                {
                    File.Delete(downloadPath);
                    Directory.Delete(extractTempPath, true);
                    _logger.LogDebug("Temporary update files cleaned up");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to clean up temporary files, but update succeeded");
                }

                // Step 7: Schedule restart
                _logger.LogInformation("Update applied successfully. Initiating agent restart in 5 seconds...");
                _logger.LogInformation("Backup available at: {BackupPath}", backupPath);
                
                // Give time for log to be written
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                
                // Exit the process - systemd will restart the agent
                _logger.LogInformation("Exiting agent for restart. New version: {Version}", version);
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Update failed. Attempting rollback from backup: {BackupPath}", backupPath);
                
                // Attempt rollback
                try
                {
                    RollbackFromBackup(backupPath);
                    _logger.LogInformation("Rollback completed successfully");
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogCritical(rollbackEx, "CRITICAL: Rollback failed. Manual intervention required. Backup location: {BackupPath}", backupPath);
                }
                
                throw;
            }
        }
        finally
        {
            _updateLock.Release();
        }
    }

    /// <summary>
    /// Creates a backup of the current agent directory with timestamp.
    /// </summary>
    private string CreateBackup()
    {
        try
        {
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
            var backupPath = Path.Combine(_backupBaseDirectory, $"agent.{timestamp}");
            
            _logger.LogInformation("Creating backup at: {BackupPath}", backupPath);
            
            if (Directory.Exists(backupPath))
            {
                _logger.LogWarning("Backup directory already exists, deleting: {BackupPath}", backupPath);
                Directory.Delete(backupPath, true);
            }
            
            Directory.CreateDirectory(backupPath);
            CopyDirectory(_agentDirectory, backupPath, overwrite: false);
            
            _logger.LogInformation("Backup created successfully: {BackupPath}", backupPath);
            return backupPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create backup of agent directory");
            return string.Empty;
        }
    }

    /// <summary>
    /// Rollback agent to a previous backup.
    /// </summary>
    private void RollbackFromBackup(string backupPath)
    {
        if (string.IsNullOrEmpty(backupPath) || !Directory.Exists(backupPath))
        {
            throw new DirectoryNotFoundException($"Backup directory not found: {backupPath}");
        }

        _logger.LogInformation("Rolling back from backup: {BackupPath}", backupPath);
        
        // Delete current agent directory contents (except backup directory itself)
        foreach (var file in Directory.GetFiles(_agentDirectory))
        {
            File.Delete(file);
        }
        foreach (var dir in Directory.GetDirectories(_agentDirectory))
        {
            if (!dir.StartsWith(_backupBaseDirectory))
            {
                Directory.Delete(dir, true);
            }
        }
        
        // Restore from backup
        CopyDirectory(backupPath, _agentDirectory, overwrite: true);
        
        _logger.LogInformation("Rollback completed successfully from: {BackupPath}", backupPath);
    }

    /// <summary>
    /// Recursively copies a directory and its contents.
    /// </summary>
    private void CopyDirectory(string sourceDir, string destDir, bool overwrite)
    {
        var dir = new DirectoryInfo(sourceDir);
        
        if (!dir.Exists)
        {
            throw new DirectoryNotFoundException($"Source directory not found: {sourceDir}");
        }

        // Create destination directory if it doesn't exist
        Directory.CreateDirectory(destDir);

        // Copy files
        foreach (var file in dir.GetFiles())
        {
            var destFile = Path.Combine(destDir, file.Name);
            try
            {
                file.CopyTo(destFile, overwrite);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to copy file {FileName} during directory copy", file.Name);
                throw;
            }
        }

        // Recursively copy subdirectories
        foreach (var subDir in dir.GetDirectories())
        {
            var destSubDir = Path.Combine(destDir, subDir.Name);
            CopyDirectory(subDir.FullName, destSubDir, overwrite);
        }
    }

    public override void Dispose()
    {
        _sparkle?.Dispose();
        base.Dispose();
    }
}

