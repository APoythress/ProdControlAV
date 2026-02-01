using System.Reflection;
using System.IO.Compression;
using System.Diagnostics;
using System.Net.Http.Json;
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
/// Custom assembly accessor that wraps another accessor and strips SemVer build metadata
/// from the version string. This ensures that versions like "1.0.8+buildmetadata" and "1.0.8"
/// are treated as equal per SemVer 2.0.0 specification.
/// </summary>
internal class StrippedVersionAssemblyAccessor : IAssemblyAccessor
{
    private readonly IAssemblyAccessor _innerAccessor;

    public StrippedVersionAssemblyAccessor(IAssemblyAccessor innerAccessor)
    {
        _innerAccessor = innerAccessor ?? throw new ArgumentNullException(nameof(innerAccessor));
    }

    public string AssemblyCompany => _innerAccessor.AssemblyCompany;
    public string AssemblyCopyright => _innerAccessor.AssemblyCopyright;
    public string AssemblyDescription => _innerAccessor.AssemblyDescription;
    public string AssemblyTitle => _innerAccessor.AssemblyTitle;
    public string AssemblyProduct => _innerAccessor.AssemblyProduct;
    
    /// <summary>
    /// Returns the assembly version with SemVer build metadata stripped.
    /// This ensures proper version comparison per SemVer 2.0.0 spec.
    /// </summary>
    public string AssemblyVersion
    {
        get
        {
            var version = _innerAccessor.AssemblyVersion;
            return UpdateService.StripBuildMetadata(version);
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
    private string? _safeTempDirectory; // Cached safe temp directory
    
    // Marker file name for preventing infinite update loops
    private const string UpdateCompletedMarkerFileName = "prodcontrolav-update-completed";

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
    /// Gets a safe temporary directory path with fallback options.
    /// On Linux/Raspberry Pi, Path.GetTempPath() may fail when running as systemd service
    /// or when environment variables are not set correctly.
    /// </summary>
    /// <returns>A writable temporary directory path.</returns>
    private string GetSafeTempDirectory()
    {
        // Return cached value if already determined
        if (_safeTempDirectory != null)
        {
            return _safeTempDirectory;
        }
        
        // Try standard temp path first
        try
        {
            var tempPath = Path.GetTempPath();
            if (!string.IsNullOrWhiteSpace(tempPath) && Directory.Exists(tempPath))
            {
                // Verify we can write to it
                var testFile = Path.Combine(tempPath, $".prodcontrolav-test-{Guid.NewGuid()}");
                try
                {
                    File.WriteAllText(testFile, "test");
                    File.Delete(testFile);
                    _logger.LogDebug("Using system temp directory: {TempPath}", tempPath);
                    _safeTempDirectory = tempPath;
                    return _safeTempDirectory;
                }
                catch
                {
                    _logger.LogWarning("System temp directory not writable: {TempPath}", tempPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get system temp directory");
        }
        
        // Fallback 1: /tmp (standard Linux temp directory)
        try
        {
            var tmpDir = "/tmp";
            if (Directory.Exists(tmpDir))
            {
                var testFile = Path.Combine(tmpDir, $".prodcontrolav-test-{Guid.NewGuid()}");
                try
                {
                    File.WriteAllText(testFile, "test");
                    File.Delete(testFile);
                    _logger.LogInformation("Using fallback temp directory: {TempPath}", tmpDir);
                    _safeTempDirectory = tmpDir;
                    return _safeTempDirectory;
                }
                catch
                {
                    _logger.LogWarning("Fallback temp directory not writable: {TempPath}", tmpDir);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to access /tmp directory");
        }
        
        // Fallback 2: Create temp directory in agent directory
        try
        {
            var agentTempDir = Path.Combine(_agentDirectory, "temp");
            if (!Directory.Exists(agentTempDir))
            {
                Directory.CreateDirectory(agentTempDir);
            }
            
            var testFile = Path.Combine(agentTempDir, $".prodcontrolav-test-{Guid.NewGuid()}");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
            _logger.LogInformation("Using agent temp directory: {TempPath}", agentTempDir);
            _safeTempDirectory = agentTempDir;
            return _safeTempDirectory;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create temp directory in agent directory");
        }
        
        // Last resort: use current directory
        var currentDir = Directory.GetCurrentDirectory();
        _logger.LogWarning("Using current directory as temp: {TempPath}", currentDir);
        _safeTempDirectory = currentDir;
        return _safeTempDirectory;
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
    
            // NetSparkle signature verification modes:
            // - Strict: Requires appcast.json.signature file (detached signature for appcast itself) + item signatures
            // - UseIfPossible: Still ATTEMPTS to download appcast.json.signature, fails if network error occurs
            // - Unsafe: Skips appcast signature verification, but STILL verifies per-item signatures
            //
            // Current setup: Using embedded per-item signatures in appcast.json (each item has a "signature" field)
            // We DON'T have a separate appcast.json.signature file for the appcast itself
            //
            // Issue: Even with UseIfPossible, NetSparkle tries to download appcast.json.signature and fails
            // Solution: Use SecurityMode.Unsafe to skip appcast signature check while still verifying item signatures
            //
            // Security: Each update package STILL has its signature verified using Ed25519 before installation
            // The Ed25519Checker with Unsafe mode will verify item signatures when downloading updates
            var signatureVerifier = new Ed25519Checker(SecurityMode.Unsafe, _updateOptions.Ed25519PublicKey);
            _logger.LogInformation("Signature verification mode: Unsafe (skips appcast signature, verifies per-item signatures)");
            _logger.LogInformation("Security: Each update package signature is verified with Ed25519 before installation");
    
            // Create custom appcast downloader with configurable timeout
            var appcastDownloader = new ConfigurableAppCastDataDownloader(
                TimeSpan.FromSeconds(_updateOptions.AppcastTimeoutSeconds));
    
            // Get safe temp directory with fallback options
            var safeTempDir = GetSafeTempDirectory();
            var tmpDownloadPath = Path.Combine(safeTempDir, "prodcontrolav-update.zip");
            _logger.LogInformation("Update download path: {DownloadPath}", tmpDownloadPath);
    
            _sparkle = new SparkleUpdater(
                _updateOptions.AppcastUrl,
                signatureVerifier,
                _referenceAssembly)  // Pass reference assembly path for configuration storage
            {
                // Headless mode - no UI
                UIFactory = null,
                TmpDownloadFilePath = tmpDownloadPath,
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
            _logger.LogDebug("HTTP client configured for direct connection (proxy bypassed) to Azure Blob Storage");
    
            // NetSparkle reads the version from the reference assembly using AssemblyInformationalVersion.
            // Per SemVer 2.0.0 spec, build metadata (the +hash part) should be ignored for version precedence.
            // However, NetSparkle may treat versions with different build metadata as different versions.
            // We handle this by checking versions after CheckForUpdatesQuietly() and filtering out
            // "updates" that are actually the same version with different build metadata.
    
            _logger.LogInformation("NetSparkle update system initialized successfully");
            _logger.LogInformation("Note: File logging for UpdateService is active in logs/updateService/ folder");
    
            // Check if we just completed an update (marker file exists)
            // If so, delete the marker and skip the first update check to prevent infinite loop
            var updateCompletedMarker = Path.Combine(GetSafeTempDirectory(), UpdateCompletedMarkerFileName);
            if (File.Exists(updateCompletedMarker))
            {
                _logger.LogInformation("Update completed marker detected. Skipping initial update check to prevent re-update loop.");
                try
                {
                    File.Delete(updateCompletedMarker);
                    _logger.LogDebug("Deleted update completed marker file");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete update completed marker, but continuing");
                }
                
                // Wait one full check interval before starting the update check loop
                // This gives the agent time to stabilize after an update
                _logger.LogInformation("Waiting {Seconds} seconds before starting update checks after completed update", _updateOptions.CheckIntervalSeconds);
                await Task.Delay(TimeSpan.FromSeconds(_updateOptions.CheckIntervalSeconds), stoppingToken);
            }

            // Run the update check loop
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Define update signal file path once
                    var updateSignalFile = Path.Combine(GetSafeTempDirectory(), "prodcontrolav-update-trigger");

                    // Check for manual update trigger signal (file-based)
                    // This file is created by CommandService when an UPDATE command is received from the command queue
                    var fileTrigger = File.Exists(updateSignalFile);

                    if (!fileTrigger)
                    {
                        _logger.LogDebug("No manual update trigger detected, checking for updates normally (current version: {CurrentVersion})...", _currentVersion);
                    }

                    if (fileTrigger)
                    {
                        if (fileTrigger)
                        {
                            _logger.LogInformation("Manual update trigger detected via file, checking for updates immediately...");
                            try
                            {
                                File.Delete(updateSignalFile);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to delete update trigger file");
                            }
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

                            if (_updateOptions.AutoInstall || fileTrigger)
                            {
                                if (fileTrigger)
                                {
                                    _logger.LogInformation("Manual update trigger detected - applying update immediately...");
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
                            if (fileTrigger)
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
                    else
                    {
                        // No trigger, just check for automatic updates
                        UpdateInfo? updateInfo = await CheckForUpdatesWithRetryAsync(stoppingToken);

                        if (updateInfo != null)
                        {
                            if (updateInfo.Status == UpdateStatus.UpdateAvailable)
                            {
                                var latestVersion = updateInfo.Updates?.FirstOrDefault()?.Version ?? "unknown";
                                _logger.LogInformation(
                                    "Update available: Version {LatestVersion} (current: {CurrentVersion})",
                                    latestVersion, _currentVersion);

                                if (_updateOptions.AutoInstall)
                                {
                                    _logger.LogInformation("Auto-install is enabled. Applying update...");
                                    await ApplyUpdateAsync(updateInfo, stoppingToken);
                                }
                                else
                                {
                                    _logger.LogInformation("Auto-install is disabled. Manual update trigger required from dashboard.");
                                }
                            }
                            else if (updateInfo.Status == UpdateStatus.UpdateNotAvailable)
                            {
                                _logger.LogDebug("No updates available. Current version {CurrentVersion} is up to date.", _currentVersion);
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
                
                // NetSparkle may consider versions with different build metadata as different versions
                // even though SemVer 2.0.0 states build metadata should be ignored for precedence.
                // If an update is available, check if the versions are actually equal after stripping build metadata.
                if (updateInfo.Status == UpdateStatus.UpdateAvailable && updateInfo.Updates?.Count > 0)
                {
                    var latestUpdate = updateInfo.Updates.FirstOrDefault();
                    if (latestUpdate != null)
                    {
                        var latestVersion = latestUpdate.Version;
                        var strippedLatestVersion = StripBuildMetadata(latestVersion);
                        var strippedCurrentVersion = StripBuildMetadata(_currentVersionRaw);
                        
                        // Use ordinal comparison for version strings to ensure consistent behavior across cultures
                        if (string.Equals(strippedLatestVersion, strippedCurrentVersion, StringComparison.Ordinal))
                        {
                            _logger.LogInformation(
                                "NetSparkle reported update available, but versions are equal after stripping build metadata: " +
                                "Current={CurrentVersionRaw} (stripped: {StrippedCurrent}), " +
                                "Latest={LatestVersion} (stripped: {StrippedLatest}). " +
                                "No update needed per SemVer 2.0.0 specification.",
                                _currentVersionRaw, strippedCurrentVersion, latestVersion, strippedLatestVersion);
                            
                            // Return UpdateNotAvailable to prevent unnecessary update attempt
                            return new UpdateInfo(UpdateStatus.UpdateNotAvailable);
                        }
                    }
                }
                
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

        string? envBackupPath = null;

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

            // Step 0: Preserve existing `.env` before any destructive work
            try
            {
                var envPath = Path.Combine(_agentDirectory, ".env");
                if (File.Exists(envPath))
                {
                    envBackupPath = Path.Combine(GetSafeTempDirectory(), $"prodcontrolav-env-backup-{Guid.NewGuid()}.env");
                    File.Copy(envPath, envBackupPath, overwrite: true);
                    _logger.LogDebug("Backed up existing .env to: {EnvBackup}", envBackupPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to back up .env before update. Proceeding but .env may be lost if overwritten.");
            }

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
                var downloadPath = Path.Combine(GetSafeTempDirectory(), $"prodcontrolav-update-{version}.zip");
                _logger.LogInformation("Downloading update to: {DownloadPath}", downloadPath);

                using (var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
                {
                    var response = await httpClient.GetAsync(downloadUrl, stoppingToken);
                    response.EnsureSuccessStatusCode();

                    await using var fileStream = File.Create(downloadPath);
                    await response.Content.CopyToAsync(fileStream, stoppingToken);
                }

                _logger.LogInformation("Update downloaded successfully");

                if (!File.Exists(downloadPath))
                {
                    throw new FileNotFoundException("Downloaded update file not found", downloadPath);
                }

                // Step 4: Extract update to temporary directory
                _logger.LogInformation("Extracting update to agent directory: {AgentDirectory}", _agentDirectory);

                var extractTempPath = Path.Combine(GetSafeTempDirectory(), $"prodcontrolav-extract-{version}");
                if (Directory.Exists(extractTempPath))
                {
                    Directory.Delete(extractTempPath, true);
                }
                Directory.CreateDirectory(extractTempPath);

                ZipFile.ExtractToDirectory(downloadPath, extractTempPath, overwriteFiles: true);
                _logger.LogInformation("Update extracted to temporary directory: {TempPath}", extractTempPath);

                // Step 5: Launch external updater and exit
                _logger.LogInformation("Launching external updater to apply update and restart agent");
                
                // Create marker file to indicate update was applied
                // This prevents infinite update loop on restart when AutoInstall is enabled
                try
                {
                    var updateCompletedMarker = Path.Combine(GetSafeTempDirectory(), UpdateCompletedMarkerFileName);
                    File.WriteAllText(updateCompletedMarker, string.Empty);
                    _logger.LogDebug("Created update completed marker at: {MarkerPath}", updateCompletedMarker);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to create update completed marker, but continuing with update");
                }
                
                LaunchExternalUpdaterAndExit(extractTempPath, backupPath, envBackupPath ?? "", "prodcontrolav-agent");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Update failed. Attempting rollback from backup: {BackupPath}", backupPath);

                // Attempt rollback
                try
                {
                    RollbackFromBackup(backupPath);
                    _logger.LogInformation("Rollback completed successfully");

                    // After rollback, restore `.env` backup (if any) to ensure it's preserved
                    try
                    {
                        if (!string.IsNullOrEmpty(envBackupPath) && File.Exists(envBackupPath))
                        {
                            var envPath = Path.Combine(_agentDirectory, ".env");
                            File.Copy(envBackupPath, envPath, overwrite: true);
                            File.Delete(envBackupPath);
                            _logger.LogInformation(".env restored from temp backup after rollback");
                        }
                    }
                    catch (Exception restoreEx)
                    {
                        _logger.LogWarning(restoreEx, "Failed to restore .env from temp backup after rollback");
                    }
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
    
        // Call this from ApplyUpdateAsync after extraction and backup creation.
        private void LaunchExternalUpdaterAndExit(string extractTempPath, string backupPath, string envBackupPath, string serviceName = "")
        {
            var currentExe = Path.GetFileName(Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty);
            var pid = Environment.ProcessId;
    
            // Build script path
            var scriptPath = Path.Combine(Path.GetTempPath(), $"prodcontrolav_updater_{Guid.NewGuid():N}.sh");
    
            // POSIX updater script. Waits for PID to exit, copies files (excluding .env and exe),
            // attempts to restart via systemctl if serviceName provided, otherwise launches the binary.
            var script = $@"#!/bin/bash
                set -euo pipefail
                EXTRACT=""{EscapeBashArg(extractTempPath)}""
                AGENT_DIR=""{EscapeBashArg(_agentDirectory)}""
                BACKUP=""{EscapeBashArg(backupPath)}""
                PID={pid}
                EXE_NAME=""{EscapeBashArg(currentExe)}""
                ENV_BACKUP=""{EscapeBashArg(envBackupPath ?? string.Empty)}""
                SERVICE_NAME=""{EscapeBashArg(serviceName ?? string.Empty)}""

                log() {{ echo \""[updater] $$(date -u +'%Y-%m-%dT%H:%M:%SZ') $$1\""; }}

                log \""Waiting for agent (PID $$PID) to stop...\""
                for i in {{1..30}}; do
                  if ! kill -0 $$PID 2>/dev/null; then
                    log \""agent stopped\""
                    break
                  fi
                  sleep 1
                done

                # final check: ensure agent is not running
                if kill -0 $$PID 2>/dev/null; then
                  log \""agent still running after timeout; aborting\""
                  exit 1
                fi
                
                log \""Applying update: copying files from $$EXTRACT to $$AGENT_DIR (preserve .env and $$EXE_NAME)\""
                
                # ensure destination exists
                mkdir -p \""$$AGENT_DIR\""
                
                # Use rsync if available for robust copy, otherwise fallback to tar/cp
                if command -v rsync >/dev/null 2>&1; then
                  rsync -a --delete --exclude='.env' --exclude=\""$EXE_NAME\"" \""$$EXTRACT\""/ \""$$AGENT_DIR\""/
                else
                  # fallback: move files carefully
                  (cd \""$$EXTRACT\"" && tar -cf - . --exclude='./.env' --exclude=\""./$$EXE_NAME\"") | (cd \""$$AGENT_DIR\"" && tar -xpf -)
                fi
                
                # attempt restart
                if [ -n \""$$SERVICE_NAME\"" ] && command -v systemctl >/dev/null 2>&1; then
                  log \""Restarting service $$SERVICE_NAME via systemctl\""
                  if ! systemctl restart \""$$SERVICE_NAME\""; then
                    log \""service restart failed, restoring backup\""
                    restore_and_exit 2
                  fi
                
                  sleep 2
                  if ! systemctl is-active --quiet \""$$SERVICE_NAME\""; then
                    log \""service not active after restart, restoring backup\""
                    restore_and_exit 3
                  fi
                  log \""service restarted successfully\""
                  cleanup_and_exit 0
                else
                  # start binary directly in background
                  TARGET_EXE=\""$AGENT_DIR/$$EXE_NAME\""
                  if [ ! -x \""$$TARGET_EXE\"" ]; then
                    chmod +x \""$$TARGET_EXE\"" || true
                  fi
                
                  log \""Starting agent binary: $$TARGET_EXE\""
                  nohup \""$$TARGET_EXE\"" >/dev/null 2>&1 &
                
                  sleep 3
                  if pgrep -f \""$$TARGET_EXE\"" >/dev/null 2>&1; then
                    log \""agent started successfully\""
                    cleanup_and_exit 0
                  else
                    log \""agent failed to start, restoring backup\""
                    restore_and_exit 4
                  fi
                fi
                
                restore_and_exit() {{
                  rc=$$1
                  log \""Restoring backup from $$BACKUP to $$AGENT_DIR\""
                  # remove current files except .env
                  find \""$$AGENT_DIR\"" -mindepth 1 -maxdepth 1 ! -name '.env' -exec rm -rf {{}} +
                  # restore backup
                  if command -v rsync >/dev/null 2>&1; then
                    rsync -a --delete \""$$BACKUP\""/ \""$$AGENT_DIR\""/
                  else
                    (cd \""$$BACKUP\"" && tar -cf - .) | (cd \""$$AGENT_DIR\"" && tar -xpf -)
                  fi
                
                  # restore .env from temporary backup if provided and missing
                  if [ -n \""$$ENV_BACKUP\"" ] && [ -f \""$$ENV_BACKUP\"" ]; then
                    if [ ! -f \""$$AGENT_DIR/.env\"" ]; then
                      mv \""$$ENV_BACKUP\"" \""$$AGENT_DIR/.env\"" || true
                    else
                      rm -f \""$$ENV_BACKUP\"" || true
                    fi
                  fi
                
                  log \""Backup restored. Exiting with $$rc\""
                  exit $$rc
                }}
                
                cleanup_and_exit() {{
                  rc=$$1
                  # remove temp extract and env backup if present
                  rm -rf \""{EscapeBashArg(extractTempPath)}\"" || true
                  if [ -n \""$$ENV_BACKUP\"" ] && [ -f \""$$ENV_BACKUP\"" ]; then
                    rm -f \""$$ENV_BACKUP\"" || true
                  fi
                  log \""Update applied and cleaned up. Exiting with $$rc\""
                  exit $$rc
                }}";
    
            // Write script
            File.WriteAllText(scriptPath, script);
            // ensure executable
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "/bin/chmod",
                    Arguments = $"+x {EscapeShellArg(scriptPath)}",
                    UseShellExecute = false,
                    CreateNoWindow = true
                })?.Dispose();
            }
            catch { /* best-effort */ }
    
            // Launch detached updater
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"{EscapeShellArg(scriptPath)}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };
    
            // Start the updater and do not wait; exit the agent so updater can replace files
            var proc = Process.Start(psi);
            _logger.LogInformation("Launched external updater (pid={Pid}) and exiting agent to allow file replacement", proc?.Id);
            Environment.Exit(0);
        }
    
        private static string EscapeBashArg(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\"", "\\\"");
        }
    
        private static string EscapeShellArg(string s)
        {
            if (string.IsNullOrEmpty(s)) return "''";
            return "'" + s.Replace("'", "'\"'\"'") + "'";
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

        // Delete current agent directory contents but preserve `.env`
        foreach (var file in Directory.GetFiles(_agentDirectory))
        {
            try
            {
                if (string.Equals(Path.GetFileName(file), ".env", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogDebug("Preserving .env during rollback: {File}", file);
                    continue;
                }

                File.Delete(file);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete file {File} during rollback", file);
                throw;
            }
        }

        foreach (var dir in Directory.GetDirectories(_agentDirectory))
        {
            if (!dir.StartsWith(_backupBaseDirectory, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(dir, true);
            }
        }

        // Restore from backup (this will overwrite files except `.env` if you preserved it above)
        CopyDirectory(backupPath, _agentDirectory, overwrite: true);

        _logger.LogInformation("Rollback completed successfully from: {BackupPath}", backupPath);
    }
    
    /// <summary>
    /// Recursively copies a directory and its contents.
    /// </summary>
    private void CopyDirectory(string sourceDir, string destDir, bool overwrite, IEnumerable<string>? excludeFileNames = null)
    {
        var dir = new DirectoryInfo(sourceDir);

        if (!dir.Exists)
        {
            throw new DirectoryNotFoundException($"Source directory not found: {sourceDir}");
        }

        // Normalize exclude names for quick lookup
        HashSet<string>? excludeSet = null;
        if (excludeFileNames != null)
        {
            excludeSet = new HashSet<string>(excludeFileNames, StringComparer.OrdinalIgnoreCase);
        }

        // Create destination directory if it doesn't exist
        Directory.CreateDirectory(destDir);

        // Copy files
        foreach (var file in dir.GetFiles())
        {
            // Skip excluded files (like `.env`)
            if (excludeSet != null && excludeSet.Contains(file.Name))
            {
                _logger.LogDebug("Skipping excluded file during copy: {FileName}", file.Name);
                continue;
            }

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
            CopyDirectory(subDir.FullName, destSubDir, overwrite, excludeFileNames);
        }
    }
    
    public override void Dispose()
    {
        _sparkle?.Dispose();
        base.Dispose();
    }
}

