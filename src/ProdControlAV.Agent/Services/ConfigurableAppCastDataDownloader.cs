using NetSparkleUpdater.Downloaders;
using NetSparkleUpdater.Interfaces;

namespace ProdControlAV.Agent.Services;

/// <summary>
/// Custom AppCast data downloader that allows configuring the HTTP timeout and handler settings.
/// NetSparkle's default WebRequestAppCastDataDownloader uses a 100-second timeout and default
/// HttpClient configuration which may have proxy issues or connection pooling problems.
/// This implementation configures an optimized HttpClient with:
/// - Configurable timeout (default: 30 seconds)
/// - No proxy (direct connection)
/// - Proper connection pooling for better performance
/// </summary>
internal class ConfigurableAppCastDataDownloader : WebRequestAppCastDataDownloader
{
    private readonly TimeSpan _timeout;

    /// <summary>
    /// Creates a new configurable appcast data downloader.
    /// </summary>
    /// <param name="timeout">The timeout for HTTP requests. Defaults to 30 seconds.</param>
    public ConfigurableAppCastDataDownloader(TimeSpan timeout)
    {
        _timeout = timeout;
    }

    /// <summary>
    /// Creates an HttpClient with optimized configuration for direct Azure Blob Storage access.
    /// This overrides the base class method to:
    /// - Set custom timeout
    /// - Bypass system proxy (use direct connection)
    /// - Configure proper connection pooling
    /// - Set connection limits for better performance
    /// </summary>
    protected override HttpClient CreateHttpClient()
    {
        // Use SocketsHttpHandler for better performance and control
        var handler = new SocketsHttpHandler
        {
            // Bypass any system proxy - connect directly to Azure Blob Storage
            // This prevents proxy-related timeouts and connection issues
            UseProxy = false,
            Proxy = null,
            
            // Connection pooling settings
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            
            // Allow multiple connections for parallel requests
            MaxConnectionsPerServer = 10,
            
            // Enable automatic decompression for better performance
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        };
        
        var client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = _timeout
        };
        
        // Set User-Agent to identify the agent
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ProdControlAV-Agent/1.0");
        
        return client;
    }
}
