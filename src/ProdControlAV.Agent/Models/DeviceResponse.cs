namespace ProdControlAV.Agent.Models;

/// <summary>
/// Standard response model returned by all device connections in the shared TCP framework.
/// Provides a uniform contract regardless of the underlying device protocol.
/// </summary>
public sealed class DeviceResponse
{
    /// <summary>Whether the command completed successfully (status code 200–299).</summary>
    public bool Success { get; init; }

    /// <summary>Protocol-level status code (e.g. 200 for HyperDeck OK, 500 for error).</summary>
    public int StatusCode { get; init; }

    /// <summary>Human-readable status message or device status text.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Key/value fields included in the response (case-insensitive keys).</summary>
    public Dictionary<string, string> Data { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}
