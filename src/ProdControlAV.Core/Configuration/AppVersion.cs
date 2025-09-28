namespace ProdControlAV.Core.Configuration;

public static class AppVersion
{
    public const string Version = "0.1.00";
    public const string FullVersion = "v0.1.00-alpha";
    
    /// <summary>
    /// Gets the current application version
    /// </summary>
    public static string GetVersion() => Version;
    
    /// <summary>
    /// Gets the full version string with pre-release identifier
    /// </summary>
    public static string GetFullVersion() => FullVersion;
}