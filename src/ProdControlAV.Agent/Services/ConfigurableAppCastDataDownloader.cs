using System.Text;
using NetSparkleUpdater.Interfaces;

namespace ProdControlAV.Agent.Services;

/// <summary>
/// Custom AppCast data downloader that implements IAppCastDataDownloader directly.
/// NetSparkle's default WebRequestAppCastDataDownloader uses a hardcoded 100-second timeout
/// and caches the HttpClient internally, making it impossible to override the timeout.
/// This implementation creates an optimized HttpClient with:
/// - Configurable timeout (e.g., 30 seconds instead of 100)
/// - No proxy (direct connection to Azure Blob Storage)
/// - Proper connection pooling for better performance
/// - Full control over HTTP behavior
/// </summary>
internal class ConfigurableAppCastDataDownloader : IAppCastDataDownloader
{
    private readonly HttpClient _httpClient;
    private NetSparkleUpdater.Interfaces.ILogger? _logWriter;

    /// <summary>
    /// Gets or sets the logger for diagnostic output.
    /// </summary>
    public NetSparkleUpdater.Interfaces.ILogger? LogWriter
    {
        get => _logWriter;
        set => _logWriter = value;
    }

    /// <summary>
    /// Creates a new configurable appcast data downloader.
    /// </summary>
    /// <param name="timeout">The timeout for HTTP requests.</param>
    public ConfigurableAppCastDataDownloader(TimeSpan timeout)
    {
        // Create HttpClient with optimized configuration for Azure Blob Storage
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
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
            
            // Increase timeouts for slow/unreliable network connections
            // ConnectTimeout specifically for the TCP connection handshake
            ConnectTimeout = TimeSpan.FromSeconds(30),
            
            // Allow more time for DNS resolution and initial connection
            // This is critical for Raspberry Pi devices with slow/unstable networks
            ResponseDrainTimeout = TimeSpan.FromSeconds(10)
        };
        
        _httpClient = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = timeout
        };
        
        // Set User-Agent to identify the agent
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ProdControlAV-Agent/1.0");
        
        // Add Accept header for JSON content
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    /// <summary>
    /// Downloads the appcast data from the specified URL synchronously.
    /// Required by IAppCastDataDownloader interface.
    /// Note: This method blocks on async operations which is required by the interface.
    /// NetSparkle's SparkleUpdater calls this in a background thread, so blocking is acceptable.
    /// </summary>
    /// <param name="url">The URL to download from.</param>
    /// <returns>The appcast data as a string, or empty string if download fails.</returns>
    public string DownloadAndGetAppCastData(string url)
    {
        var startTime = DateTime.UtcNow;
        try
        {
            _logWriter?.PrintMessage("Starting appcast download from: {0}", url);
            _logWriter?.PrintMessage("HTTP client timeout configured: {0} seconds", _httpClient.Timeout.TotalSeconds);
            _logWriter?.PrintMessage("Connect timeout: 30 seconds, DNS resolution included in request timeout");
            
            // Using GetAwaiter().GetResult() here is safe because:
            // 1. This method is called by NetSparkle in a background thread (not UI thread)
            // 2. The interface requires a synchronous method
            // 3. There's no synchronization context to deadlock against
            _logWriter?.PrintMessage("Initiating HTTP GET request...");
            using var response = _httpClient.GetAsync(url).GetAwaiter().GetResult();
            
            var connectionTime = (DateTime.UtcNow - startTime).TotalSeconds;
            _logWriter?.PrintMessage("Connection established in {0:F2} seconds", connectionTime);
            _logWriter?.PrintMessage("Response status: {0} {1}", (int)response.StatusCode, response.StatusCode);
            
            if (!response.IsSuccessStatusCode)
            {
                _logWriter?.PrintMessage("Failed to download appcast: HTTP {0}", response.StatusCode);
                return string.Empty;
            }
            
            _logWriter?.PrintMessage("Reading response content...");
            var content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            
            var totalTime = (DateTime.UtcNow - startTime).TotalSeconds;
            _logWriter?.PrintMessage("Successfully downloaded appcast ({0} bytes) in {1:F2} seconds", content?.Length ?? 0, totalTime);
            
            return content ?? string.Empty;
        }
        catch (TaskCanceledException ex)
        {
            var elapsedTime = (DateTime.UtcNow - startTime).TotalSeconds;
            _logWriter?.PrintMessage("Timeout after {0:F2} seconds (configured: {1}s): {2}", 
                elapsedTime, _httpClient.Timeout.TotalSeconds, ex.Message);
            _logWriter?.PrintMessage("Network may be slow or DNS resolution failed. Consider increasing AppcastTimeoutSeconds to 90-120 seconds.");
            return string.Empty;
        }
        catch (HttpRequestException ex)
        {
            var elapsedTime = (DateTime.UtcNow - startTime).TotalSeconds;
            _logWriter?.PrintMessage("HTTP error after {0:F2} seconds: {1}", elapsedTime, ex.Message);
            
            // Provide specific guidance based on the error
            if (ex.InnerException != null)
            {
                _logWriter?.PrintMessage("Inner exception: {0}", ex.InnerException.Message);
            }
            
            if (ex.Message.Contains("Name or service not known") || ex.Message.Contains("No such host"))
            {
                _logWriter?.PrintMessage("DNS resolution failed. Check network connectivity and DNS configuration.");
            }
            else if (ex.Message.Contains("Connection refused"))
            {
                _logWriter?.PrintMessage("Connection refused. Check if firewall is blocking access to Azure Blob Storage.");
            }
            else if (ex.Message.Contains("SSL") || ex.Message.Contains("TLS"))
            {
                _logWriter?.PrintMessage("SSL/TLS error. Check certificate configuration and system time.");
            }
            
            return string.Empty;
        }
        catch (Exception ex)
        {
            var elapsedTime = (DateTime.UtcNow - startTime).TotalSeconds;
            _logWriter?.PrintMessage("Unexpected error after {0:F2} seconds: {1}", elapsedTime, ex.Message);
            _logWriter?.PrintMessage("Exception type: {0}", ex.GetType().Name);
            return string.Empty;
        }
    }

    /// <summary>
    /// Downloads the appcast data from the specified URL asynchronously.
    /// Required by IAppCastDataDownloader interface.
    /// </summary>
    /// <param name="url">The URL to download from.</param>
    /// <returns>The appcast data as a string, or empty string if download fails.</returns>
    public async Task<string> DownloadAndGetAppCastDataAsync(string url)
    {
        try
        {
            _logWriter?.PrintMessage("Downloading appcast from: {0}", url);
            _logWriter?.PrintMessage("HTTP client timeout configured: {0} seconds", _httpClient.Timeout.TotalSeconds);
            
            using var response = await _httpClient.GetAsync(url).ConfigureAwait(false);
            
            _logWriter?.PrintMessage("Response status: {0} {1}", (int)response.StatusCode, response.StatusCode);
            
            if (!response.IsSuccessStatusCode)
            {
                _logWriter?.PrintMessage("Failed to download appcast: HTTP {0}", response.StatusCode);
                return string.Empty;
            }
            
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            _logWriter?.PrintMessage("Successfully downloaded appcast ({0} bytes)", content?.Length ?? 0);
            
            return content ?? string.Empty;
        }
        catch (TaskCanceledException ex)
        {
            _logWriter?.PrintMessage("Timeout downloading appcast after {0} seconds: {1}", _httpClient.Timeout.TotalSeconds, ex.Message);
            return string.Empty;
        }
        catch (HttpRequestException ex)
        {
            _logWriter?.PrintMessage("HTTP error downloading appcast: {0}", ex.Message);
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logWriter?.PrintMessage("Unexpected error downloading appcast: {0}", ex.Message);
            return string.Empty;
        }
    }

    /// <summary>
    /// Gets the encoding used for the appcast data.
    /// Required by IAppCastDataDownloader interface.
    /// </summary>
    /// <returns>UTF8 encoding for JSON appcast files.</returns>
    public Encoding GetAppCastEncoding()
    {
        return Encoding.UTF8;
    }

    /// <summary>
    /// Disposes the HTTP client.
    /// </summary>
    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
