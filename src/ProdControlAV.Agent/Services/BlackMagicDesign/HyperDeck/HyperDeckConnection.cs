using ProdControlAV.Agent.Models;

namespace ProdControlAV.Agent.Services;

/// <summary>
/// Connection state of a HyperDeck device.
/// Maps to <see cref="DeviceConnectionState"/> from <see cref="BaseTcpDeviceConnection"/>.
/// </summary>
public enum HyperDeckConnectionState
{
    Disconnected,
    Connecting,
    Connected
}

/// <summary>
/// A parsed response block from a HyperDeck device.
/// Used internally by <see cref="HyperDeckConnection.ParseBlock"/> and its unit tests.
/// Response blocks are terminated by a blank line.
/// </summary>
public sealed class HyperDeckResponse
{
    /// <summary>HyperDeck status code (e.g. 200 for success).</summary>
    public int StatusCode { get; init; }

    /// <summary>Status text following the code (e.g. "ok").</summary>
    public string StatusText { get; init; } = string.Empty;

    /// <summary>Key/value fields included in the response block.</summary>
    public Dictionary<string, string> Fields { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Maintains a persistent TCP connection to a single HyperDeck device.
/// Implements the HyperDeck Ethernet Protocol (ASCII line-based, port 9993).
///
/// Inherits all TCP transport infrastructure (read/write loops, reconnection,
/// command serialisation) from <see cref="BaseTcpDeviceConnection"/> and supplies
/// the HyperDeck-specific response parser.
/// </summary>
public sealed class HyperDeckConnection : BaseTcpDeviceConnection
{
    /// <inheritdoc/>
    protected override string DeviceTypeName => "HyperDeck";

    public HyperDeckConnection(string host, int port, ILogger logger)
        : base(host, port, logger) { }

    /// <summary>
    /// Maps the base-class <see cref="DeviceConnectionState"/> to the HyperDeck-specific enum
    /// for backward compatibility with existing call sites.
    /// </summary>
    public HyperDeckConnectionState ConnectionState => State switch
    {
        DeviceConnectionState.Connected  => HyperDeckConnectionState.Connected,
        DeviceConnectionState.Connecting => HyperDeckConnectionState.Connecting,
        _                                => HyperDeckConnectionState.Disconnected
    };

    // ── Protocol hook ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    protected override DeviceResponse ParseResponseBlock(IReadOnlyList<string> lines)
    {
        var raw = ParseBlock(lines);
        return new DeviceResponse
        {
            Success    = raw.StatusCode is >= 200 and < 300,
            StatusCode = raw.StatusCode,
            Message    = raw.StatusText,
            Data       = raw.Fields
        };
    }

    // ── HyperDeck response parser ─────────────────────────────────────────────

    /// <summary>
    /// Parses a collected block of raw lines into a <see cref="HyperDeckResponse"/>.
    /// First line: "{code} {text}" — subsequent lines: "{key}: {value}".
    /// </summary>
    internal static HyperDeckResponse ParseBlock(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
            return new HyperDeckResponse();

        int statusCode = 0;
        string statusText = string.Empty;

        var firstLine = lines[0];
        var spaceIdx = firstLine.IndexOf(' ');
        if (spaceIdx > 0 && int.TryParse(firstLine.AsSpan(0, spaceIdx), out var code))
        {
            statusCode = code;
            statusText = firstLine[(spaceIdx + 1)..].Trim();
        }
        else
        {
            statusText = firstLine.Trim();
        }

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < lines.Count; i++)
        {
            var colonIdx = lines[i].IndexOf(':');
            if (colonIdx > 0)
            {
                var key   = lines[i][..colonIdx].Trim();
                var value = lines[i][(colonIdx + 1)..].Trim();
                fields[key] = value;
            }
        }

        return new HyperDeckResponse
        {
            StatusCode = statusCode,
            StatusText = statusText,
            Fields     = fields
        };
    }
}
