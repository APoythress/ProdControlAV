using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

namespace ProdControlAV.Agent.Services;

/// <summary>
/// Connection state of a HyperDeck device.
/// </summary>
public enum HyperDeckConnectionState
{
    Disconnected,
    Connecting,
    Connected
}

/// <summary>
/// A parsed response block from a HyperDeck device.
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
/// Supports automatic reconnection and serialised command/response correlation.
/// </summary>
public sealed class HyperDeckConnection : IAsyncDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly ILogger _logger;

    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(5);

    // Exponential-backoff delays for reconnection (ms)
    private static readonly int[] ReconnectDelaysMs = { 500, 1000, 2000, 5000, 10000 };

    private TcpClient? _tcpClient;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    // Outbound command queue processed by the write loop
    private readonly Channel<OutboundCommand> _outboundChannel =
        Channel.CreateUnbounded<OutboundCommand>(new UnboundedChannelOptions { SingleReader = true });

    // One command in flight at a time
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    // The TCS waiting for the current response; read loop resolves it
    private TaskCompletionSource<HyperDeckResponse>? _pendingResponse;

    private CancellationTokenSource _cts = new();
    private Task _readTask = Task.CompletedTask;
    private Task _writeTask = Task.CompletedTask;
    private bool _disposed;

    /// <summary>Current connection state.</summary>
    public HyperDeckConnectionState ConnectionState { get; private set; } =
        HyperDeckConnectionState.Disconnected;

    public HyperDeckConnection(string host, int port, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("Host cannot be null or empty.", nameof(host));
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1 and 65535.");

        _host = host;
        _port = port;
        _logger = logger;
    }

    /// <summary>
    /// Establishes the TCP connection and starts the background read/write loops.
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        await ConnectAsync(ct);
        _readTask = ReadLoopAsync(_cts.Token);
        _writeTask = WriteLoopAsync(_cts.Token);
    }

    /// <summary>
    /// Sends a text command to the HyperDeck and awaits the response block.
    /// Only one command may be in flight at a time; additional callers are serialised.
    /// </summary>
    /// <exception cref="TimeoutException">Thrown when no response is received within 5 seconds.</exception>
    public async Task<HyperDeckResponse> SendCommandAsync(string commandText, CancellationToken ct = default)
    {
        await _sendLock.WaitAsync(ct);
        try
        {
            var tcs = new TaskCompletionSource<HyperDeckResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            await _outboundChannel.Writer.WriteAsync(new OutboundCommand(commandText, tcs), ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(CommandTimeout);

            try
            {
                return await tcs.Task.WaitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                var timeoutEx = new TimeoutException(
                    $"HyperDeck command '{commandText}' timed out after {CommandTimeout.TotalSeconds}s");
                _logger.LogWarning(
                    "HyperDeck command '{Command}' timed out at {Host}:{Port}",
                    commandText, _host, _port);
                tcs.TrySetException(timeoutEx);
                throw timeoutEx;
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    // ── connection ────────────────────────────────────────────────────────────

    private async Task ConnectAsync(CancellationToken ct)
    {
        ConnectionState = HyperDeckConnectionState.Connecting;
        _tcpClient?.Dispose();
        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(_host, _port, ct);

        var stream = _tcpClient.GetStream();
        _reader = new StreamReader(stream, Encoding.ASCII);
        _writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true };

        ConnectionState = HyperDeckConnectionState.Connected;
        _logger.LogInformation("Connected to HyperDeck at {Host}:{Port}", _host, _port);
    }

    private async Task ReconnectAsync(CancellationToken ct)
    {
        ConnectionState = HyperDeckConnectionState.Disconnected;

        for (int attempt = 0; !ct.IsCancellationRequested; attempt++)
        {
            var delayMs = ReconnectDelaysMs[Math.Min(attempt, ReconnectDelaysMs.Length - 1)];
            _logger.LogInformation(
                "Reconnect attempt {Attempt} to HyperDeck {Host}:{Port} in {Delay}ms",
                attempt + 1, _host, _port, delayMs);

            try
            {
                await Task.Delay(delayMs, ct);
                await ConnectAsync(ct);

                // Restart the read loop on the new stream
                _readTask = ReadLoopAsync(_cts.Token);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex, "Reconnect attempt {Attempt} to HyperDeck {Host}:{Port} failed",
                    attempt + 1, _host, _port);
            }
        }
    }

    // ── write loop ────────────────────────────────────────────────────────────

    private async Task WriteLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var cmd in _outboundChannel.Reader.ReadAllAsync(ct))
            {
                try
                {
                    // Register pending response BEFORE sending to avoid a race with the read loop
                    _pendingResponse = cmd.Response;
                    await _writer!.WriteAsync(cmd.Text + "\r\n");
                    _logger.LogDebug(
                        "Sent HyperDeck command: '{Command}' to {Host}:{Port}",
                        cmd.Text, _host, _port);
                }
                catch (Exception ex)
                {
                    cmd.Response.TrySetException(ex);
                    _pendingResponse = null;
                    _logger.LogError(
                        ex, "Error sending HyperDeck command '{Command}' to {Host}:{Port}",
                        cmd.Text, _host, _port);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down – expected
        }
    }

    // ── read loop ─────────────────────────────────────────────────────────────

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var lines = new List<string>();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                string? line;
                try
                {
                    line = await _reader!.ReadLineAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error reading from HyperDeck {Host}:{Port}", _host, _port);
                    FailPendingCommand(ex);
                    await ReconnectAsync(ct);
                    return; // New read loop started inside ReconnectAsync
                }

                if (line == null)
                {
                    // Remote closed the connection
                    _logger.LogWarning("HyperDeck {Host}:{Port} closed the connection", _host, _port);
                    var closeEx = new IOException("HyperDeck closed the connection.");
                    FailPendingCommand(closeEx);
                    await ReconnectAsync(ct);
                    return;
                }

                if (line.Length == 0)
                {
                    // Blank line = end of response block
                    if (lines.Count > 0)
                    {
                        var response = ParseBlock(lines);
                        lines.Clear();
                        ResolveOrUpdateState(response);
                    }
                }
                else
                {
                    lines.Add(line);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down – expected
        }
    }

    private void FailPendingCommand(Exception ex)
    {
        var pending = Interlocked.Exchange(ref _pendingResponse, null);
        pending?.TrySetException(ex);
    }

    private void ResolveOrUpdateState(HyperDeckResponse response)
    {
        var pending = Interlocked.Exchange(ref _pendingResponse, null);
        if (pending != null)
        {
            _logger.LogDebug(
                "HyperDeck response from {Host}:{Port}: {StatusCode} {StatusText}",
                _host, _port, response.StatusCode, response.StatusText);
            pending.TrySetResult(response);
        }
        else
        {
            // Unsolicited device update (e.g. transport state change)
            _logger.LogDebug(
                "Unsolicited HyperDeck update from {Host}:{Port}: {StatusCode} {StatusText}",
                _host, _port, response.StatusCode, response.StatusText);
        }
    }

    // ── response parser ───────────────────────────────────────────────────────

    /// <summary>
    /// Parses a collected block of lines into a <see cref="HyperDeckResponse"/>.
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
                var key = lines[i][..colonIdx].Trim();
                var value = lines[i][(colonIdx + 1)..].Trim();
                fields[key] = value;
            }
        }

        return new HyperDeckResponse
        {
            StatusCode = statusCode,
            StatusText = statusText,
            Fields = fields
        };
    }

    // ── disposal ──────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        _outboundChannel.Writer.TryComplete();

        try { await _readTask.WaitAsync(TimeSpan.FromSeconds(2)); } catch { /* best effort */ }
        try { await _writeTask.WaitAsync(TimeSpan.FromSeconds(2)); } catch { /* best effort */ }

        _reader?.Dispose();
        _writer?.Dispose();
        _tcpClient?.Dispose();
        _cts.Dispose();
        _sendLock.Dispose();
    }

    // ── internal types ────────────────────────────────────────────────────────

    private sealed record OutboundCommand(
        string Text,
        TaskCompletionSource<HyperDeckResponse> Response);
}
