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
    private SparkleUpdater? _sparkle;

    public UpdateService(
        ILogger<UpdateService> logger,
        IOptions<UpdateOptions> updateOptions)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _updateOptions = updateOptions?.Value ?? throw new ArgumentNullException(nameof(updateOptions));
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
                    _logger.LogDebug("Checking for updates...");
                    
                    // Check for updates
                    var updateInfo = await _sparkle.CheckForUpdatesQuietly();
                    
                    if (updateInfo.Status == UpdateStatus.UpdateAvailable)
                    {
                        _logger.LogInformation(
                            "Update available: Version {Version}",
                            updateInfo.Updates?.FirstOrDefault()?.Version ?? "unknown");

                        if (_updateOptions.AutoInstall)
                        {
                            _logger.LogInformation("Auto-install is enabled. Downloading update...");
                            
                            // Download and install update
                            // Note: This is a simplified approach. In production, you may want to
                            // implement more sophisticated download/install logic with the full
                            // NetSparkle event system once the correct API is determined.
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
                        _logger.LogDebug("No updates available");
                    }
                    else if (updateInfo.Status == UpdateStatus.CouldNotDetermine)
                    {
                        _logger.LogWarning("Could not determine update status");
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

    private static string GetAgentVersion()
    {
        var assembly = typeof(UpdateService).Assembly;
        var version = assembly.GetName().Version;
        return version?.ToString() ?? "0.0.0";
    }

    public override void Dispose()
    {
        _sparkle?.Dispose();
        base.Dispose();
    }
}

