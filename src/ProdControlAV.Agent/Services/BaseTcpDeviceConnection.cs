using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using ProdControlAV.Agent.Interfaces;
using ProdControlAV.Agent.Models;

namespace ProdControlAV.Agent.Services;

/// <summary>
/// Connection state tracked by <see cref="BaseTcpDeviceConnection"/> and
/// <see cref="BaseUdpDeviceConnection"/>.
/// </summary>
public enum DeviceConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    /// <summary>Too many consecutive failures; connection is backing off before retrying.</summary>
    Faulted
}

/// <summary>
/// Reusable abstract base class that implements all TCP transport infrastructure for
/// network-controlled AV devices (connection lifecycle, read/write loops, command
/// queuing, response correlation, reconnection with exponential backoff, logging).
///
/// Device-specific subclasses must override <see cref="ParseResponseBlock"/> to convert
/// the raw line buffer into a <see cref="DeviceResponse"/>.  Optionally override
/// <see cref="IsEndOfBlock"/>, <see cref="FormatCommand"/>, or <see cref="StreamEncoding"/>
/// for protocol variations.
///
/// Example hierarchy:
/// <code>
/// BaseTcpDeviceConnection
///     ├── HyperDeckConnection   (ASCII line-based, port 9993)
///     ├── AtemConnection        (future)
///     └── TelnetDeviceConnection (future)
/// </code>
/// </summary>
public abstract class BaseTcpDeviceConnection : IDeviceConnection, IAsyncDisposable
{
    /// <summary>Default command response timeout used by the convenience overload.</summary>
    private static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Exponential-backoff delays for reconnection (ms): 500 ms → 10 s.</summary>
    private static readonly int[] ReconnectDelaysMs = { 500, 1000, 2000, 5000, 10000 };

    /// <summary>Device host address.</summary>
    protected readonly string Host;

    /// <summary>Device TCP port.</summary>
    protected readonly int Port;

    /// <summary>Shared logger instance.</summary>
    protected readonly ILogger Logger;

    private TcpClient? _tcpClient;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    // Outbound command queue consumed by the write loop (single-reader channel).
    private readonly Channel<OutboundCommand> _outboundChannel =
        Channel.CreateUnbounded<OutboundCommand>(new UnboundedChannelOptions { SingleReader = true });

    // Serialises callers: only one command may be in flight at a time.
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    // The TCS for the currently outstanding command; resolved by the read loop.
    private TaskCompletionSource<DeviceResponse>? _pendingResponse;

    private CancellationTokenSource _cts = new();
    private Task _readTask = Task.CompletedTask;
    private Task _writeTask = Task.CompletedTask;
    private bool _disposed;

    // Tracks connection state; updated on the connection/reconnection path.
    private DeviceConnectionState _state = DeviceConnectionState.Disconnected;

    /// <summary>Current connection state.</summary>
    protected DeviceConnectionState State => _state;

    /// <inheritdoc/>
    public bool IsConnected => _state == DeviceConnectionState.Connected;

    /// <summary>
    /// A human-readable label used in log messages (e.g. "HyperDeck", "ATEM").
    /// Override in subclasses for clearer diagnostics.
    /// </summary>
    protected virtual string DeviceTypeName => "Device";

    /// <summary>
    /// Initialises the base connection.
    /// </summary>
    /// <param name="host">Device IP address or hostname.</param>
    /// <param name="port">Device TCP port (1–65535).</param>
    /// <param name="logger">Logger instance.</param>
    protected BaseTcpDeviceConnection(string host, int port, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("Host cannot be null or empty.", nameof(host));
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1 and 65535.");

        Host = host;
        Port = port;
        Logger = logger;
    }

    // ── IDeviceConnection ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken ct = default)
    {
        await ConnectAsync(ct);
        _readTask = ReadLoopAsync(_cts.Token);
        _writeTask = WriteLoopAsync(_cts.Token);
    }

    /// <inheritdoc/>
    public Task<DeviceResponse> SendCommandAsync(string command, TimeSpan timeout, CancellationToken ct = default)
        => SendCommandCoreAsync(command, timeout, ct);

    /// <summary>
    /// Convenience overload that uses the default 5-second timeout.
    /// </summary>
    public Task<DeviceResponse> SendCommandAsync(string command, CancellationToken ct = default)
        => SendCommandCoreAsync(command, DefaultCommandTimeout, ct);

    /// <inheritdoc/>
    public Task DisconnectAsync()
    {
        _cts.Cancel();
        _state = DeviceConnectionState.Disconnected;
        return Task.CompletedTask;
    }

    // ── Abstract / virtual protocol hooks ────────────────────────────────────

    /// <summary>
    /// Parse a collected block of raw lines from the device into a <see cref="DeviceResponse"/>.
    /// Called by the read loop when <see cref="IsEndOfBlock"/> returns true.
    /// </summary>
    protected abstract DeviceResponse ParseResponseBlock(IReadOnlyList<string> lines);

    /// <summary>
    /// Returns true when the supplied <paramref name="line"/> marks the end of a response block.
    /// Defaults to blank-line termination (common for ASCII line-based protocols).
    /// </summary>
    protected virtual bool IsEndOfBlock(string? line) => string.IsNullOrEmpty(line);

    /// <summary>
    /// Formats a command string before it is written to the network stream.
    /// Defaults to appending CRLF, which is standard for most AV device protocols.
    /// </summary>
    protected virtual string FormatCommand(string command) => command + "\r\n";

    /// <summary>
    /// Character encoding used for the device stream.  Defaults to ASCII.
    /// </summary>
    protected virtual Encoding StreamEncoding => Encoding.ASCII;

    // ── Connection management ─────────────────────────────────────────────────

    private async Task ConnectAsync(CancellationToken ct)
    {
        _state = DeviceConnectionState.Connecting;
        _tcpClient?.Dispose();
        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(Host, Port, ct);

        var stream = _tcpClient.GetStream();
        _reader = new StreamReader(stream, StreamEncoding);
        _writer = new StreamWriter(stream, StreamEncoding) { AutoFlush = true };

        _state = DeviceConnectionState.Connected;
        Logger.LogInformation(
            "{DeviceType} connected at {Host}:{Port}", DeviceTypeName, Host, Port);
    }

    private async Task ReconnectAsync(CancellationToken ct)
    {
        _state = DeviceConnectionState.Disconnected;

        for (int attempt = 0; !ct.IsCancellationRequested; attempt++)
        {
            var delayMs = ReconnectDelaysMs[Math.Min(attempt, ReconnectDelaysMs.Length - 1)];
            Logger.LogInformation(
                "{DeviceType} reconnect attempt {Attempt} to {Host}:{Port} in {Delay}ms",
                DeviceTypeName, attempt + 1, Host, Port, delayMs);

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
                Logger.LogWarning(
                    ex, "{DeviceType} reconnect attempt {Attempt} to {Host}:{Port} failed",
                    DeviceTypeName, attempt + 1, Host, Port);
            }
        }
    }

    // ── Write loop ────────────────────────────────────────────────────────────

    private async Task WriteLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var cmd in _outboundChannel.Reader.ReadAllAsync(ct))
            {
                try
                {
                    // Register the pending response BEFORE writing so the read loop
                    // cannot resolve it before we have stored the TCS.
                    _pendingResponse = cmd.Response;
                    await _writer!.WriteAsync(FormatCommand(cmd.Text));
                    Logger.LogDebug(
                        "Sent {DeviceType} command: '{Command}' to {Host}:{Port}",
                        DeviceTypeName, cmd.Text, Host, Port);
                }
                catch (Exception ex)
                {
                    cmd.Response.TrySetException(ex);
                    _pendingResponse = null;
                    Logger.LogError(
                        ex, "Error sending {DeviceType} command '{Command}' to {Host}:{Port}",
                        DeviceTypeName, cmd.Text, Host, Port);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down – expected
        }
    }

    // ── Read loop ─────────────────────────────────────────────────────────────

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
                    Logger.LogError(
                        ex, "Error reading from {DeviceType} {Host}:{Port}", DeviceTypeName, Host, Port);
                    FailPendingCommand(ex);
                    await ReconnectAsync(ct);
                    return; // New read loop started inside ReconnectAsync
                }

                if (line == null)
                {
                    // Remote closed the connection
                    Logger.LogWarning(
                        "{DeviceType} {Host}:{Port} closed the connection", DeviceTypeName, Host, Port);
                    var closeEx = new IOException($"{DeviceTypeName} closed the connection.");
                    FailPendingCommand(closeEx);
                    await ReconnectAsync(ct);
                    return;
                }

                if (IsEndOfBlock(line))
                {
                    if (lines.Count > 0)
                    {
                        var response = ParseResponseBlock(lines);
                        lines.Clear();
                        DeliverResponse(response);
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

    private void DeliverResponse(DeviceResponse response)
    {
        var pending = Interlocked.Exchange(ref _pendingResponse, null);
        if (pending != null)
        {
            Logger.LogDebug(
                "{DeviceType} response from {Host}:{Port}: {StatusCode} {Message}",
                DeviceTypeName, Host, Port, response.StatusCode, response.Message);
            pending.TrySetResult(response);
        }
        else
        {
            // Unsolicited device notification (e.g. transport state change)
            Logger.LogDebug(
                "Unsolicited {DeviceType} update from {Host}:{Port}: {StatusCode} {Message}",
                DeviceTypeName, Host, Port, response.StatusCode, response.Message);
        }
    }

    // ── SendCommandCoreAsync ──────────────────────────────────────────────────

    private async Task<DeviceResponse> SendCommandCoreAsync(
        string command, TimeSpan timeout, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct);
        try
        {
            var tcs = new TaskCompletionSource<DeviceResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            await _outboundChannel.Writer.WriteAsync(new OutboundCommand(command, tcs), ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);

            try
            {
                return await tcs.Task.WaitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                var timeoutEx = new TimeoutException(
                    $"{DeviceTypeName} command '{command}' timed out after {timeout.TotalSeconds}s");
                Logger.LogWarning(
                    "{DeviceType} command '{Command}' timed out at {Host}:{Port}",
                    DeviceTypeName, command, Host, Port);
                tcs.TrySetException(timeoutEx);
                throw timeoutEx;
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    // ── Disposal ──────────────────────────────────────────────────────────────

    /// <inheritdoc/>
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

    // ── Private types ─────────────────────────────────────────────────────────

    private sealed record OutboundCommand(
        string Text,
        TaskCompletionSource<DeviceResponse> Response);
}
