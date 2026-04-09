using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using ProdControlAV.Agent.Interfaces;
using ProdControlAV.Agent.Models;

namespace ProdControlAV.Agent.Services;

/// <summary>
/// Encapsulates a received UDP datagram together with the source endpoint.
/// </summary>
public sealed class ReceivedDatagram
{
    public byte[] Data { get; init; } = Array.Empty<byte>();
    public IPEndPoint RemoteEndPoint { get; init; } = new IPEndPoint(IPAddress.Any, 0);
}

/// <summary>
/// Protocol context passed to encoding/decoding hooks so subclasses can access
/// session state without needing to cast the base class.
/// </summary>
public sealed class UdpProtocolContext
{
    /// <summary>Current outbound packet sequence number (incremented by the base after each send).</summary>
    public int OutboundSequence { get; set; }

    /// <summary>Last inbound sequence number seen from the device.</summary>
    public int LastReceivedSequence { get; set; }

    /// <summary>Arbitrary session identifier extracted during the handshake (e.g. ATEM session ID).</summary>
    public ushort SessionId { get; set; }
}

/// <summary>
/// Reusable abstract base class that implements UDP transport infrastructure for
/// network-controlled AV devices (session lifecycle, receive loop, outbound queue,
/// response correlation, retransmission/ack helpers, re-handshake with exponential backoff,
/// keepalive, logging).
///
/// Device-specific subclasses must override methods to:
/// <list type="bullet">
///   <item><description>build UDP datagrams from commands (<see cref="BuildDatagramFromCommand"/>)</description></item>
///   <item><description>parse inbound datagrams into <see cref="DeviceResponse"/> or protocol events (<see cref="TryParseDeviceResponse"/>)</description></item>
///   <item><description>define session handshake/keepalive behaviour (if required)</description></item>
/// </list>
///
/// Example hierarchy:
/// <code>
/// BaseUdpDeviceConnection
///     ├── AtemUdpConnection       (binary UDP protocol, port 9910)
///     ├── ArtNetConnection        (future)
///     └── CustomUdpDeviceConnection (future)
/// </code>
/// </summary>
public abstract class BaseUdpDeviceConnection : IDeviceConnection, IAsyncDisposable
{
    // ── Constants ─────────────────────────────────────────────────────────────

    /// <summary>Default command response timeout.</summary>
    private static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Exponential-backoff delays for reconnection (ms): 500 ms → 10 s.</summary>
    private static readonly int[] ReconnectDelaysMs = { 500, 1000, 2000, 5000, 10000 };

    /// <summary>How long to wait for background tasks to clean up on disposal.</summary>
    private static readonly TimeSpan TaskCleanupTimeout = TimeSpan.FromSeconds(2);

    // ── Protected properties ──────────────────────────────────────────────────

    /// <summary>How long to wait for the handshake to complete before treating it as failed.</summary>
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Device host address.</summary>
    protected readonly string Host;

    /// <summary>Device UDP port.</summary>
    protected readonly int Port;

    /// <summary>Shared logger instance.</summary>
    protected readonly ILogger Logger;

    /// <summary>Shared mutable protocol context (sequence numbers, session ID, etc.).</summary>
    protected readonly UdpProtocolContext ProtocolContext = new();

    // ── Private state ─────────────────────────────────────────────────────────

    private UdpClient? _udpClient;
    private IPEndPoint _remoteEndPoint;

    // Outbound command queue consumed by the send loop (single-reader channel).
    private readonly Channel<OutboundUdpCommand> _outboundChannel =
        Channel.CreateUnbounded<OutboundUdpCommand>(new UnboundedChannelOptions { SingleReader = true });

    // Serialises callers: only one command may be in flight at a time.
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    // The TCS for the currently outstanding command; resolved by the receive loop.
    private TaskCompletionSource<DeviceResponse>? _pendingResponse;

    // Tracks outbound datagrams awaiting ACK { sequence → (datagram, attempt count, tcs) }.
    private readonly Dictionary<int, PendingAckEntry> _pendingAcks = new();
    private readonly SemaphoreSlim _pendingAcksLock = new(1, 1);

    private CancellationTokenSource _cts = new();
    private Task _receiveTask = Task.CompletedTask;
    private Task _sendTask = Task.CompletedTask;
    private Task _keepAliveTask = Task.CompletedTask;
    private Task _reliabilityTask = Task.CompletedTask;
    private bool _disposed;

    // Session state
    private volatile DeviceConnectionState _state = DeviceConnectionState.Disconnected;

    // ── Public surface ────────────────────────────────────────────────────────

    /// <summary>Current connection state.</summary>
    protected DeviceConnectionState State => _state;

    /// <inheritdoc/>
    public bool IsConnected => _state == DeviceConnectionState.Connected;

    /// <summary>
    /// A human-readable label used in log messages (e.g. "ATEM", "ArtNet").
    /// Override in subclasses for clearer diagnostics.
    /// </summary>
    protected virtual string DeviceTypeName => "UdpDevice";

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Initialises the base UDP connection.
    /// </summary>
    /// <param name="host">Device IP address or hostname.</param>
    /// <param name="port">Device UDP port (1–65535).</param>
    /// <param name="logger">Logger instance.</param>
    protected BaseUdpDeviceConnection(string host, int port, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("Host cannot be null or empty.", nameof(host));
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1 and 65535.");

        Host = host;
        Port = port;
        Logger = logger;
        // For connection tracking; actual DNS resolution happens when UdpClient.Connect is called.
        _remoteEndPoint = new IPEndPoint(IPAddress.Any, port);
    }

    // ── IDeviceConnection ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken ct = default)
    {
        SetState(DeviceConnectionState.Connecting);
        Logger.LogInformation("{DeviceType} session starting for {Host}:{Port}", DeviceTypeName, Host, Port);

        _udpClient?.Dispose();
        _udpClient = new UdpClient();
        _udpClient.Connect(Host, Port);

        // Start receive and send loops BEFORE the handshake so the receive loop can
        // dispatch the handshake response datagram back to PerformHandshakeAsync.
        _receiveTask = ReceiveLoopAsync(_cts.Token);
        _sendTask = SendLoopAsync(_cts.Token);

        if (RequiresHandshake)
        {
            await PerformHandshakeAsync(ct);
        }
        else
        {
            SetState(DeviceConnectionState.Connected);
            Logger.LogInformation("{DeviceType} session ready (no handshake required) for {Host}:{Port}",
                DeviceTypeName, Host, Port);
        }

        if (RequiresKeepAlive)
            _keepAliveTask = KeepAliveLoopAsync(_cts.Token);
        if (UsesReliability)
            _reliabilityTask = ReliabilityLoopAsync(_cts.Token);
    }

    /// <inheritdoc/>
    public Task<DeviceResponse> SendCommandAsync(string command, TimeSpan timeout, CancellationToken ct = default)
        => SendCommandCoreAsync(command, timeout, ct);

    /// <summary>Convenience overload that uses the default 5-second timeout.</summary>
    public Task<DeviceResponse> SendCommandAsync(string command, CancellationToken ct = default)
        => SendCommandCoreAsync(command, DefaultCommandTimeout, ct);

    /// <inheritdoc/>
    public Task DisconnectAsync()
    {
        _cts.Cancel();
        SetState(DeviceConnectionState.Disconnected);

        var disconnectEx = new IOException($"{DeviceTypeName} at {Host}:{Port} was disconnected.");
        FailPendingCommand(disconnectEx);
        DrainOutboundChannel(disconnectEx);

        Logger.LogInformation("{DeviceType} session disconnected ({Host}:{Port})", DeviceTypeName, Host, Port);
        return Task.CompletedTask;
    }

    // ── Abstract / virtual protocol hooks ────────────────────────────────────

    // ---------- Encoding/Decoding -------------------------------------------

    /// <summary>
    /// Builds a UDP datagram payload from the logical command string.
    /// </summary>
    /// <param name="command">Logical command (device-specific format).</param>
    /// <param name="ctx">Shared protocol context (may be used to inject sequence numbers etc.).</param>
    /// <returns>Raw bytes to be sent as a single datagram.</returns>
    protected abstract byte[] BuildDatagramFromCommand(string command, UdpProtocolContext ctx);

    /// <summary>
    /// Attempts to parse an inbound datagram into a <see cref="DeviceResponse"/>.
    /// Return <c>false</c> if the datagram is not a device-response (e.g. it is a keepalive ack).
    /// </summary>
    protected abstract bool TryParseDeviceResponse(ReceivedDatagram rx, out DeviceResponse response);

    /// <summary>
    /// Optional: returns true when the given datagram is the response to the current pending command.
    /// The default implementation returns true for every response (serial, one-command-at-a-time model).
    /// Override in subclasses that can correlate by sequence number or command type.
    /// </summary>
    protected virtual bool IsDeviceResponseForPendingCommand(ReceivedDatagram rx, UdpProtocolContext ctx)
        => true;

    // ---------- Session / Handshake -----------------------------------------

    /// <summary>Whether this protocol requires an explicit handshake before sending commands.</summary>
    protected virtual bool RequiresHandshake => false;

    /// <summary>Sends the handshake datagram(s) to the device.</summary>
    protected virtual Task SendHandshakeAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>Returns true when <paramref name="rx"/> is a valid handshake completion datagram.</summary>
    protected virtual bool IsHandshakeResponse(ReceivedDatagram rx) => false;

    /// <summary>
    /// Extracts session-level data from the handshake response (e.g. session ID).
    /// Called once after <see cref="IsHandshakeResponse"/> returns true.
    /// </summary>
    protected virtual void ApplyHandshakeResponse(ReceivedDatagram rx) { }

    /// <summary>
    /// Called during the handshake phase for packets that are not the final handshake
    /// completion response (i.e. <see cref="IsHandshakeResponse"/> returned false).
    /// Subclasses can override this to handle intermediate handshake packets and send
    /// protocol-specific replies (e.g. the ATEM three-way SYN→SYN-ACK→ACK sequence).
    /// </summary>
    /// <returns>
    /// <c>true</c> if the datagram was consumed by the handshake logic and normal
    /// dispatch should be skipped; <c>false</c> to fall through to the normal receive path.
    /// </returns>
    protected virtual Task<bool> HandleHandshakeIntermediatePacketAsync(ReceivedDatagram rx, CancellationToken ct)
        => Task.FromResult(false);

    // ---------- Keepalive ---------------------------------------------------

    /// <summary>Whether this protocol requires periodic keepalive datagrams.</summary>
    protected virtual bool RequiresKeepAlive => false;

    /// <summary>Interval between keepalive datagrams.</summary>
    protected virtual TimeSpan KeepAliveInterval => TimeSpan.FromSeconds(1);

    /// <summary>Sends a single keepalive datagram.</summary>
    protected virtual Task SendKeepAliveAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Returns true when <paramref name="rx"/> is a keepalive acknowledgement.
    /// If true, the datagram is consumed by the keepalive logic and not passed to response correlation.
    /// </summary>
    protected virtual bool IsKeepAliveResponse(ReceivedDatagram rx) => false;

    // ---------- Reliability (ACK / retransmit) ------------------------------

    /// <summary>Whether this protocol uses ACK-based reliability at the datagram level.</summary>
    protected virtual bool UsesReliability => false;

    /// <summary>How long to wait for an ACK before retransmitting.</summary>
    protected virtual TimeSpan AckTimeout => TimeSpan.FromMilliseconds(500);

    /// <summary>Maximum number of retransmit attempts before failing the command.</summary>
    protected virtual int MaxRetries => 3;

    /// <summary>Builds an ACK datagram in response to a received datagram.</summary>
    protected virtual byte[] BuildAckDatagram(UdpProtocolContext ctx, ReceivedDatagram rx)
        => Array.Empty<byte>();

    /// <summary>Returns true when <paramref name="rx"/> is an inbound ACK datagram.</summary>
    protected virtual bool IsAckDatagram(ReceivedDatagram rx) => false;

    /// <summary>Applies an inbound ACK — marks the matching outbound datagram as acknowledged.</summary>
    protected virtual void ApplyAck(ReceivedDatagram rx) { }

    /// <summary>Extracts the sequence number from an inbound datagram (for duplicate/ordering detection).</summary>
    protected virtual int GetDatagramSequence(ReceivedDatagram rx) => 0;

    /// <summary>Extracts the sequence number being acknowledged from an inbound ACK datagram.</summary>
    protected virtual int GetAckedSequence(ReceivedDatagram rx) => 0;

    /// <summary>
    /// When true, receiving an ACK for a pending datagram automatically resolves
    /// the corresponding command's <see cref="DeviceResponse"/> TCS with a success result.
    /// Set to <c>true</c> for protocols (like ATEM) where the ACK itself is the "response"
    /// rather than a subsequent data packet.
    /// </summary>
    protected virtual bool AckResolvesResponse => false;

    // ── Session state helpers ─────────────────────────────────────────────────

    private void SetState(DeviceConnectionState newState)
    {
        var previous = _state;
        _state = newState;
        if (previous != newState)
            OnDeviceStateChanged(newState);
    }

    /// <summary>
    /// Called when the connection state transitions to a new value.
    /// Override in subclasses to propagate state changes to higher-level abstractions.
    /// </summary>
    protected virtual void OnDeviceStateChanged(DeviceConnectionState newState) { }

    /// <summary>
    /// Sends a raw datagram byte array directly to the connected device endpoint.
    /// Available to subclasses for custom protocol frames (e.g. handshake ACKs, keepalives).
    /// </summary>
    protected Task SendRawDatagramAsync(byte[] datagram, CancellationToken ct)
        => SendDatagramAsync(datagram, ct);

    private async Task PerformHandshakeAsync(CancellationToken ct)
    {
        Logger.LogInformation("{DeviceType} starting handshake with {Host}:{Port}", DeviceTypeName, Host, Port);

        // using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // timeoutCts.CancelAfter(HandshakeTimeout);

        // TODO - I do not want a run continously here - single handshak with a timeout only
        var handshakeTcs = new TaskCompletionSource<ReceivedDatagram>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Install a one-shot handshake listener before sending (avoids race).
        _handshakeTcs = handshakeTcs;

        try
        {
            // This is where the loop is happening - we only need to fire off a single handshake
            // - wait for response
            // - then validate if we are connected
            Logger.LogDebug(" ===== Sending Handshake from BaseUdpConnection.PerformHandshakeAsync() =====");
            var sendTask = SendHandshakeAsync(ct);

            var rx = await handshakeTcs.Task.WaitAsync(ct);

            // Response received – stop the send loop.
            // timeoutCts.Cancel();
            // try { await sendTask; }
            // catch (OperationCanceledException) { }
            // catch (Exception ex)
            // {
            //     Logger.LogWarning(ex, "{DeviceType} handshake send loop for {Host}:{Port} ended with an error",
            //         DeviceTypeName, Host, Port);
            // }

            Logger.LogDebug("===== ApplyHandshakeResponse =====");
            ApplyHandshakeResponse(rx);

            // For reliability-enabled protocols, ACK the handshake response immediately
            // because the receive loop has not had a chance to auto-ACK it yet.
            if (UsesReliability)
            {
                Logger.LogDebug("===== BuildAckDatagram =====");
                var ack = BuildAckDatagram(ProtocolContext, rx);
                if (ack.Length > 0)
                {
                    Logger.LogDebug("===== SendAckDatagram =====");
                    await SendDatagramAsync(ack, ct);
                }
            }

            SetState(DeviceConnectionState.Connected);
            Logger.LogInformation("{DeviceType} handshake completed with {Host}:{Port}",
                DeviceTypeName, Host, Port);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            SetState(DeviceConnectionState.Faulted);
            throw new TimeoutException(
                $"{DeviceTypeName} handshake with {Host}:{Port} timed out after {HandshakeTimeout.TotalSeconds}s");
        }
        finally
        {
            _handshakeTcs = null;
        }
    }

    // One-shot TCS set during handshake; the receive loop signals it.
    private TaskCompletionSource<ReceivedDatagram>? _handshakeTcs;

    // ── Reconnect ─────────────────────────────────────────────────────────────

    private async Task ReconnectAsync(CancellationToken ct)
    {
        SetState(DeviceConnectionState.Disconnected);
        var disconnectEx = new IOException($"{DeviceTypeName} at {Host}:{Port} session lost.");
        FailPendingCommand(disconnectEx);
        DrainOutboundChannel(disconnectEx);

        for (int attempt = 0; !ct.IsCancellationRequested; attempt++)
        {
            var delayMs = ReconnectDelaysMs[Math.Min(attempt, ReconnectDelaysMs.Length - 1)];
            Logger.LogInformation(
                "{DeviceType} reconnect attempt {Attempt} to {Host}:{Port} in {Delay}ms",
                DeviceTypeName, attempt + 1, Host, Port, delayMs);

            try
            {
                await Task.Delay(delayMs, ct);

                // Re-create socket.
                _udpClient?.Dispose();
                _udpClient = new UdpClient();
                _udpClient.Connect(Host, Port);
                SetState(DeviceConnectionState.Connecting);

                // Restart receive loop on fresh socket BEFORE performing the handshake
                // so that it can dispatch the handshake response datagram.
                _receiveTask = ReceiveLoopAsync(_cts.Token);

                if (RequiresHandshake)
                    await PerformHandshakeAsync(ct);
                else
                    SetState(DeviceConnectionState.Connected);

                Logger.LogInformation("{DeviceType} reconnected to {Host}:{Port}", DeviceTypeName, Host, Port);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                SetState(DeviceConnectionState.Faulted);
                Logger.LogWarning(
                    ex, "{DeviceType} reconnect attempt {Attempt} to {Host}:{Port} failed",
                    DeviceTypeName, attempt + 1, Host, Port);
            }
        }
    }

    // ── Send loop ─────────────────────────────────────────────────────────────

    private async Task SendLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var cmd in _outboundChannel.Reader.ReadAllAsync(ct))
            {
                try
                {
                    var datagram = BuildDatagramFromCommand(cmd.Command, ProtocolContext);

                    // Register the pending response BEFORE sending to avoid a receive-before-register race.
                    _pendingResponse = cmd.Response;

                    if (UsesReliability)
                    {
                        var seq = ProtocolContext.OutboundSequence;
                        await _pendingAcksLock.WaitAsync(ct);
                        try
                        {
                            _pendingAcks[seq] = new PendingAckEntry(datagram, cmd.Response, 0, DateTimeOffset.UtcNow);
                        }
                        finally
                        {
                            _pendingAcksLock.Release();
                        }
                    }

                    await SendDatagramAsync(datagram, ct);
                    ProtocolContext.OutboundSequence++;

                    Logger.LogDebug(
                        "Sent {DeviceType} command ({Bytes} bytes) to {Host}:{Port}",
                        DeviceTypeName, datagram.Length, Host, Port);
                }
                catch (Exception ex)
                {
                    cmd.Response.TrySetException(ex);
                    _pendingResponse = null;
                    Logger.LogError(
                        ex, "Error sending {DeviceType} command to {Host}:{Port}",
                        DeviceTypeName, Host, Port);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down – expected.
        }
    }

    private async Task SendDatagramAsync(byte[] datagram, CancellationToken ct)
    {
        if (_udpClient == null) 
            throw new InvalidOperationException("UDP socket not initialised.");
        
        Logger.LogDebug("===== SendDatagramAsync from BaseUdpDeviceConnection: {datagram} =====", datagram.ToString());
        await _udpClient.SendAsync(datagram, datagram.Length).WaitAsync(ct);
    }

    // ── Receive loop ──────────────────────────────────────────────────────────

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try
                {
                    result = await _udpClient!.ReceiveAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    // Socket was disposed during reconnect – the new loop will start fresh.
                    break;
                }
                catch (Exception ex)
                {
                    Logger.LogError(
                        ex, "Receive error from {DeviceType} {Host}:{Port}", DeviceTypeName, Host, Port);
                    await ReconnectAsync(ct);
                    return; // New receive loop started inside ReconnectAsync.
                }

                Logger.LogDebug("Building ReceivedDatagram from received UDP datagram from {result}", result.ToString());
                var rx = new ReceivedDatagram
                {
                    Data = result.Buffer,
                    RemoteEndPoint = result.RemoteEndPoint
                };

                Logger.LogDebug(
                    "Received {Bytes} bytes from {DeviceType} {Host}:{Port}",
                    rx.Data.Length, DeviceTypeName, Host, Port);
                
                try
                {
                    await DispatchDatagramAsync(rx, ct);
                }
                catch (Exception ex)
                {
                    // Do not crash the loop on parse/dispatch errors.
                    Logger.LogError(
                        ex, "Error dispatching datagram from {DeviceType} {Host}:{Port}",
                        DeviceTypeName, Host, Port);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down – expected.
        }
    }

    private async Task DispatchDatagramAsync(ReceivedDatagram rx, CancellationToken ct)
    {
        // 1. Handshake completion signal.
        if (_handshakeTcs != null)
        {
            if (IsHandshakeResponse(rx))
            {
                _handshakeTcs.TrySetResult(rx);
                return;
            }

            // Allow subclasses to handle intermediate handshake packets (e.g. ATEM SYN-ACK → client ACK).
            // If the packet is consumed by the handshake logic, skip normal dispatch to avoid sending
            // incorrect reliability ACKs for protocol-internal packets.
            if (await HandleHandshakeIntermediatePacketAsync(rx, ct))
                return;
        }

        // 2. Keepalive response.
        if (RequiresKeepAlive && IsKeepAliveResponse(rx))
            return;

        // 3. ACK datagram (reliability layer).
        if (UsesReliability && IsAckDatagram(rx))
        {
            await HandleAckAsync(rx, ct);
            return;
        }

        // 4. Send an ACK if reliability is enabled (for received commands/notifications).
        if (UsesReliability)
        {
            var ack = BuildAckDatagram(ProtocolContext, rx); // returns empty array so is this even needed?!
            if (ack.Length > 0) // ack is always an empty array...
                await SendDatagramAsync(ack, ct);
        }

        // 5. Try to parse as a device response.
        if (TryParseDeviceResponse(rx, out var response))
        {
            if (IsDeviceResponseForPendingCommand(rx, ProtocolContext))
            {
                DeliverResponse(response);
            }
            else
            {
                Logger.LogDebug(
                    "Unsolicited {DeviceType} datagram from {Host}:{Port}: {StatusCode} {Message}",
                    DeviceTypeName, Host, Port, response.StatusCode, response.Message);
            }
        }

        ProtocolContext.LastReceivedSequence = GetDatagramSequence(rx);
    }

    private async Task HandleAckAsync(ReceivedDatagram rx, CancellationToken ct)
    {
        ApplyAck(rx);
        var ackedSeq = GetAckedSequence(rx);

        PendingAckEntry? entry = null;
        await _pendingAcksLock.WaitAsync(ct);
        try
        {
            if (_pendingAcks.TryGetValue(ackedSeq, out entry))
                _pendingAcks.Remove(ackedSeq);
        }
        finally
        {
            _pendingAcksLock.Release();
        }

        // For protocols where the ACK itself is the response (e.g. ATEM), resolve the TCS now.
        if (entry != null && AckResolvesResponse)
        {
            var ackResponse = new DeviceResponse { Success = true, StatusCode = 200, Message = "ACK" };
            entry.Response.TrySetResult(ackResponse);
            // _pendingResponse holds the same TCS instance – clear it so DeliverResponse
            // doesn't attempt a second delivery if a state-update packet also arrives.
            Interlocked.CompareExchange(ref _pendingResponse, null, entry.Response);
        }
    }

    // ── Reliability retransmit loop ───────────────────────────────────────────

    private async Task ReliabilityLoopAsync(CancellationToken ct)
    {
        try
        {
            // Poll at half the ACK timeout so we catch expired entries promptly.
            var pollInterval = TimeSpan.FromTicks(AckTimeout.Ticks / 2);

            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(pollInterval, ct);

                if (_state != DeviceConnectionState.Connected)
                    continue;

                // Collect entries that have exceeded AckTimeout.
                List<(int Seq, PendingAckEntry Entry)>? expired = null;

                await _pendingAcksLock.WaitAsync(ct);
                try
                {
                    var now = DateTimeOffset.UtcNow;
                    foreach (var kvp in _pendingAcks)
                    {
                        if ((now - kvp.Value.SentAt) >= AckTimeout)
                            (expired ??= new()).Add((kvp.Key, kvp.Value));
                    }
                }
                finally
                {
                    _pendingAcksLock.Release();
                }

                if (expired == null) continue;

                foreach (var (seq, entry) in expired)
                {
                    if (entry.Attempts >= MaxRetries)
                    {
                        // Max retries exceeded – fail the command and trigger reconnect.
                        await _pendingAcksLock.WaitAsync(ct);
                        try { _pendingAcks.Remove(seq); }
                        finally { _pendingAcksLock.Release(); }

                        Logger.LogWarning(
                            "{DeviceType}: max retries ({Max}) exceeded for seq {Seq} – reconnecting",
                            DeviceTypeName, MaxRetries, seq);

                        var ex = new TimeoutException(
                            $"{DeviceTypeName} command seq {seq} not acknowledged after {MaxRetries} retries.");
                        entry.Response.TrySetException(ex);
                        Interlocked.CompareExchange(ref _pendingResponse, null, entry.Response);

                        await ReconnectAsync(ct);
                    }
                    else
                    {
                        // Retransmit the datagram and update the entry.
                        Logger.LogDebug(
                            "{DeviceType}: retransmitting seq {Seq} (attempt {Attempt}/{Max})",
                            DeviceTypeName, seq, entry.Attempts + 1, MaxRetries);

                        var updated = entry with { Attempts = entry.Attempts + 1, SentAt = DateTimeOffset.UtcNow };
                        await _pendingAcksLock.WaitAsync(ct);
                        try { _pendingAcks[seq] = updated; }
                        finally { _pendingAcksLock.Release(); }

                        try
                        {
                            await SendDatagramAsync(entry.Datagram, ct);
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError(ex, "{DeviceType}: retransmit send failed for seq {Seq}", DeviceTypeName, seq);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down – expected.
        }
    }

    // ── Keepalive loop ────────────────────────────────────────────────────────

    private async Task KeepAliveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(KeepAliveInterval, ct);

                if (_state != DeviceConnectionState.Connected)
                    continue;

                try
                {
                    await SendKeepAliveAsync(ct);
                    Logger.LogDebug(
                        "{DeviceType} keepalive sent to {Host}:{Port}", DeviceTypeName, Host, Port);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(
                        ex, "{DeviceType} keepalive send failed for {Host}:{Port}", DeviceTypeName, Host, Port);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down – expected.
        }
    }

    // ── Response correlation helpers ──────────────────────────────────────────

    private void FailPendingCommand(Exception ex)
    {
        var pending = Interlocked.Exchange(ref _pendingResponse, null);
        pending?.TrySetException(ex);
    }

    private void DrainOutboundChannel(Exception ex)
    {
        // Fail any commands still queued but not yet sent.
        while (_outboundChannel.Reader.TryRead(out var cmd))
            cmd.Response.TrySetException(ex);
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

            await _outboundChannel.Writer.WriteAsync(new OutboundUdpCommand(command, tcs), ct);

            Logger.LogDebug(
                "{DeviceType} command enqueued for {Host}:{Port}: '{Command}'",
                DeviceTypeName, Host, Port, command);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);

            try
            {
                return await tcs.Task.WaitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                var timeoutEx = new TimeoutException(
                    $"{DeviceTypeName} command '{command}' to {Host}:{Port} timed out after {timeout.TotalSeconds}s");
                Logger.LogWarning(
                    "{DeviceType} command timed out at {Host}:{Port}: '{Command}'",
                    DeviceTypeName, Host, Port, command);
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

        try { await _receiveTask.WaitAsync(TaskCleanupTimeout); } catch { /* best effort */ }
        try { await _sendTask.WaitAsync(TaskCleanupTimeout); } catch { /* best effort */ }
        try { await _keepAliveTask.WaitAsync(TaskCleanupTimeout); } catch { /* best effort */ }
        try { await _reliabilityTask.WaitAsync(TaskCleanupTimeout); } catch { /* best effort */ }

        _udpClient?.Dispose();
        _cts.Dispose();
        _sendLock.Dispose();
        _pendingAcksLock.Dispose();
    }

    // ── Private types ─────────────────────────────────────────────────────────

    private sealed record OutboundUdpCommand(
        string Command,
        TaskCompletionSource<DeviceResponse> Response);

    private sealed record PendingAckEntry(
        byte[] Datagram,
        TaskCompletionSource<DeviceResponse> Response,
        int Attempts,
        DateTimeOffset SentAt);
}
