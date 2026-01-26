namespace ProdControlAV.Agent.Services;

/// <summary>
/// Configuration options for automatic updates using NetSparkle.
/// </summary>
public sealed class UpdateOptions
{
    /// <summary>
    /// Enable or disable automatic update checking.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// URL to the appcast.json manifest file in Azure Blob Storage.
    /// Example: https://yourstorageaccount.blob.core.windows.net/updates/appcast.json
    /// </summary>
    public string AppcastUrl { get; init; } = string.Empty;

    /// <summary>
    /// Ed25519 public key for verifying update signatures (base64 encoded).
    /// Generate a keypair using: dotnet tool install -g NetSparkleUpdater.Tools
    /// Then run: netsparkle-generate-keys
    /// Store the PRIVATE key in GitHub Secrets as NETSPARKLE_PRIVATE_KEY.
    /// Store the PUBLIC key here in configuration.
    /// </summary>
    public string Ed25519PublicKey { get; init; } = string.Empty;

    /// <summary>
    /// How often to check for updates (in seconds). Default: 3600 (1 hour).
    /// </summary>
    public int CheckIntervalSeconds { get; init; } = 3600;

    /// <summary>
    /// Whether to automatically download and install updates (headless mode).
    /// When true, updates are applied automatically without user interaction.
    /// When false, updates are only checked and logged (manual update required).
    /// </summary>
    public bool AutoInstall { get; init; } = true;

    /// <summary>
    /// Timeout for downloading the appcast manifest (in seconds). Default: 90 seconds.
    /// This timeout includes DNS resolution, TCP connection, TLS handshake, and data transfer.
    /// For Raspberry Pi devices on slow/unreliable networks, 90-120 seconds is recommended.
    /// The previous default of 30 seconds was too aggressive for some network conditions.
    /// </summary>
    public int AppcastTimeoutSeconds { get; init; } = 90;
}
