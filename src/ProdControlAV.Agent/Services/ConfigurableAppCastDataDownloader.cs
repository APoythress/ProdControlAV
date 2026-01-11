using NetSparkleUpdater.Downloaders;
using NetSparkleUpdater.Interfaces;

namespace ProdControlAV.Agent.Services;

/// <summary>
/// Custom AppCast data downloader that allows configuring the HTTP timeout.
/// NetSparkle's default WebRequestAppCastDataDownloader uses a 100-second timeout
/// which can be too long for Raspberry Pi deployments with unreliable network connectivity.
/// This implementation allows configuring a shorter timeout to fail faster and retry sooner.
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
    /// Creates an HttpClient with the configured timeout.
    /// This overrides the base class method to inject our custom timeout.
    /// </summary>
    protected override HttpClient CreateHttpClient()
    {
        var client = base.CreateHttpClient();
        try
        {
            client.Timeout = _timeout;
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }
}
