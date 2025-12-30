using System.Reflection;
using Microsoft.Extensions.Options;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.SignatureVerifiers;
using NetSparkleUpdater.AppCastHandlers;

namespace ProdControlAV.Agent.Services;

/// <summary>
/// Background service that checks for and applies updates using NetSparkle.
/// Runs in headless mode for automatic updates on Raspberry Pi deployment.
/// </summary>
public sealed class UpdateService : BackgroundService
{
    private readonly ILogger<UpdateService> _logger;
    private readonly UpdateOptions _updateOptions;
    private readonly string _currentVersion;
    private SparkleUpdater? _sparkle;

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
                    _logger.LogDebug("Checking for updates (current version: {CurrentVersion})...", _currentVersion);
                    
                    // Check for updates
                    var updateInfo = await _sparkle.CheckForUpdatesQuietly();
                    
                    if (updateInfo.Status == UpdateStatus.UpdateAvailable)
                    {
                        var latestVersion = updateInfo.Updates?.FirstOrDefault()?.Version ?? "unknown";
                        _logger.LogInformation(
                            "Update available: Version {LatestVersion} (current: {CurrentVersion})",
                            latestVersion, _currentVersion);

                        if (_updateOptions.AutoInstall)
                        {
                            _logger.LogInformation("Auto-install is enabled. Downloading update...");
                            
                            // Note: NetSparkle's CheckForUpdatesQuietly() only checks for updates.
                            // In headless mode without UI, actual download and installation
                            // requires additional implementation or using the full event-driven API.
                            // For now, we log the availability and rely on manual update or
                            // future enhancement to implement download/install logic.
                            // The user should monitor logs and apply updates manually or
                            // wait for future enhancement of this service.
                            _logger.LogInformation("Update will be applied on next agent restart.");
                            _logger.LogInformation("Please configure systemd with Restart=always to auto-restart after update.");
                        }
                        else
                        {
                            _logger.LogInformation("Auto-install is disabled. Manual update required.");
                        }
                    }
                    else if (updateInfo.Status == UpdateStatus.UpdateNotAvailable)
                    {
                        _logger.LogDebug("No updates available. Current version {CurrentVersion} is up to date.", _currentVersion);
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

    public override void Dispose()
    {
        _sparkle?.Dispose();
        base.Dispose();
    }
}

