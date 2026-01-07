using System.Reflection;
using System.IO.Compression;
using System.Diagnostics;
using Microsoft.Extensions.Options;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.SignatureVerifiers;
using NetSparkleUpdater.AppCastHandlers;

namespace ProdControlAV.Agent.Services;

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
    private readonly string _agentDirectory;
    private readonly string _backupBaseDirectory;
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
        _currentVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion 
            ?? assembly.GetName().Version?.ToString() 
            ?? "0.0.0";
            
        // Determine agent directory (where the agent is installed)
        _agentDirectory = Path.GetDirectoryName(assembly.Location) ?? "/opt/prodcontrolav/agent";
        
        // Backup directory
        _backupBaseDirectory = "/opt/prodcontrolav";
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
            _logger.LogInformation("Current agent version: {CurrentVersion}", _currentVersion);
            _logger.LogInformation("Agent directory: {AgentDirectory}", _agentDirectory);
            _logger.LogInformation("Backup directory: {BackupDirectory}", _backupBaseDirectory);
            _logger.LogInformation("Appcast URL: {AppcastUrl}", _updateOptions.AppcastUrl);
            _logger.LogInformation("Check interval: {Interval} seconds", _updateOptions.CheckIntervalSeconds);
            _logger.LogInformation("Auto-install: {AutoInstall}", _updateOptions.AutoInstall);

            var signatureVerifier = new Ed25519Checker(SecurityMode.Strict, _updateOptions.Ed25519PublicKey);

            _sparkle = new SparkleUpdater(
                _updateOptions.AppcastUrl,
                signatureVerifier)
            {
                // Headless mode - no UI
                UIFactory = null,
                TmpDownloadFilePath = Path.Combine(Path.GetTempPath(), "prodcontrolav-update.zip")
            };

            _logger.LogInformation("NetSparkle update system initialized successfully");

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
                    
                    // Check for updates
                    var updateInfo = await _sparkle.CheckForUpdatesQuietly();
                    
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
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error checking for updates");
                }

                // Wait for the next check interval (or shorter if manual trigger to check again soon)
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

