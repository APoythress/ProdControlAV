using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using ProdControlAV.Agent.Interfaces;
using ProdControlAV.Agent.Models;

namespace ProdControlAV.Agent.Services;

/// <summary>
/// Standalone ATEM UDP connection.
///
/// This class owns:
/// - UDP socket lifecycle
/// - handshake
/// - per-packet ACK behavior
/// - outbound command queue
/// - retransmit tracking
/// - inbound state parsing
///
/// It intentionally does NOT inherit from a generic UDP base class.
/// </summary>
public sealed class AtemUdpConnection : IAtemConnection, IAsyncDisposable
{
    public const int DefaultAtemPort = 9910;

    private const int HeaderSize = 12;
    private const int HelloPacketLength = 20;

    private const int AckTimeoutMs = 500;
    private const int MaxRetries = 5;
    private const int KeepAliveIntervalMs = 250;

    // atem-connection uses 15-bit wrapping
    private const int MaxPacketId = 1 << 15;

    // Wire-level first-byte flags (already shifted into header byte 0).
    private const byte FlagAckRequest = 0x08;
    private const byte FlagNewSession = 0x10;
    private const byte FlagAckReply   = 0x80;

    private readonly string _host;
    private readonly int _port;
    private readonly ILogger<AtemUdpConnection> _logger;

    private readonly AtemStateSnapshot _snapshot = new();
    private readonly SemaphoreSlim _cmdLock = new(1, 1);

    private readonly Channel<OutboundCommand> _sendChannel =
        Channel.CreateUnbounded<OutboundCommand>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    private readonly object _stateLock = new();

    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private Task? _sendTask;
    private Task? _keepAliveTask;
    private Task? _retransmitTask;

    private volatile bool _disposed;
    private volatile bool _isRunning;
    private volatile bool _isHandshakeComplete;

    private DeviceConnectionState _state = DeviceConnectionState.Disconnected;

    private ushort _sessionId;
    private int _lastReceivedPacketId;
    private int _nextSendPacketId = 1;
    private DateTimeOffset _lastReceivedAt = DateTimeOffset.MinValue;

    private TaskCompletionSource<bool>? _handshakeTcs;

    // ATEM in-flight reliability tracking
    private readonly ConcurrentDictionary<int, PendingPacket> _inFlight = new();

    public AtemUdpConnection(string host, ILogger<AtemUdpConnection> logger, int port = DefaultAtemPort)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("Host cannot be null or empty.", nameof(host));
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port));

        _host = host;
        _port = port;
        _logger = logger;
    }

    public bool IsConnected => _state == DeviceConnectionState.Connected;

    public AtemConnectionState ConnectionState => _state switch
    {
        DeviceConnectionState.Connected  => AtemConnectionState.Connected,
        DeviceConnectionState.Connecting => AtemConnectionState.Connecting,
        DeviceConnectionState.Faulted    => AtemConnectionState.Degraded,
        _                                => AtemConnectionState.Disconnected
    };

    public AtemState? CurrentState => _snapshot.ToAtemState();

    public event EventHandler<AtemConnectionState>? ConnectionStateChanged;
    public event EventHandler<AtemState>? StateChanged;

    public Task<bool> WaitForProgramInputAsync(TimeSpan timeout, CancellationToken ct = default)
        => _snapshot.WaitForProgramInputAsync(timeout, ct);

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (_isRunning)
            return;

        _logger.LogInformation("Starting ATEM UDP connection to {Host}:{Port}", _host, _port);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _udpClient = new UdpClient();
        _udpClient.Connect(_host, _port);

        ResetProtocolState();

        SetState(DeviceConnectionState.Connecting);
        _isRunning = true;

        _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);
        _sendTask = Task.Run(() => SendLoopAsync(_cts.Token), _cts.Token);
        _keepAliveTask = Task.Run(() => KeepAliveLoopAsync(_cts.Token), _cts.Token);
        _retransmitTask = Task.Run(() => RetransmitLoopAsync(_cts.Token), _cts.Token);

        await PerformHandshakeAsync(_cts.Token).ConfigureAwait(false);

        SetState(DeviceConnectionState.Connected);
        _logger.LogInformation("ATEM connected to {Host}:{Port} with session 0x{SessionId:X4}", _host, _port, _sessionId);
    }

    public async Task DisconnectAsync()
    {
        if (!_isRunning)
            return;

        _logger.LogInformation("Disconnecting ATEM from {Host}:{Port}", _host, _port);

        _cts?.Cancel();
        _sendChannel.Writer.TryComplete();

        _isRunning = false;
        _isHandshakeComplete = false;
        SetState(DeviceConnectionState.Disconnected);

        try
        {
            if (_receiveTask != null) await _receiveTask.ConfigureAwait(false);
            if (_sendTask != null) await _sendTask.ConfigureAwait(false);
            if (_keepAliveTask != null) await _keepAliveTask.ConfigureAwait(false);
            if (_retransmitTask != null) await _retransmitTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
        catch
        {
            // best effort shutdown
        }

        _udpClient?.Dispose();
        _udpClient = null;

        _cts?.Dispose();
        _cts = null;

        while (_sendChannel.Reader.TryRead(out var queued))
            queued.Response.TrySetException(new IOException("ATEM disconnected before command could be sent."));

        foreach (var kvp in _inFlight)
            kvp.Value.Response.TrySetException(new IOException("ATEM disconnected before ACK was received."));

        _inFlight.Clear();
    }

    public async Task<DeviceResponse> SendCommandAsync(string command, CancellationToken ct = default)
        => await SendCommandAsync(command, TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);

    public async Task<DeviceResponse> SendCommandAsync(string command, TimeSpan timeout, CancellationToken ct = default)
    {
        ThrowIfDisposed();

        if (!IsConnected)
            throw new InvalidOperationException($"ATEM is not connected (state: {ConnectionState}).");

        var tcs = new TaskCompletionSource<DeviceResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        await _sendChannel.Writer.WriteAsync(new OutboundCommand(command, tcs), ct).ConfigureAwait(false);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        try
        {
            return await tcs.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            var timeoutEx = new TimeoutException($"ATEM command '{command}' timed out after {timeout.TotalSeconds}s.");
            tcs.TrySetException(timeoutEx);
            throw timeoutEx;
        }
    }

    public async Task CutToProgramAsync(int programInputId, CancellationToken ct = default)
    {
        ValidateInputId(programInputId);
        await _cmdLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await SendCommandAsync(BuildCommandString("CPgI", 0, programInputId), ct).ConfigureAwait(false);
        }
        finally
        {
            _cmdLock.Release();
        }
    }

    public async Task FadeToProgramAsync(int programInputId, int? transitionRate = null, CancellationToken ct = default)
    {
        ValidateInputId(programInputId);
        await _cmdLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            const int me = 0;
            await SendCommandAsync(BuildCommandString("CPvI", me, programInputId), ct).ConfigureAwait(false);
            await SendCommandAsync(BuildCommandString("CTTp", me, 0), ct).ConfigureAwait(false);

            if (transitionRate.HasValue)
                await SendCommandAsync(BuildCommandString("CTMx", me, transitionRate.Value), ct).ConfigureAwait(false);

            await SendCommandAsync(BuildCommandString("DAut", me), ct).ConfigureAwait(false);
        }
        finally
        {
            _cmdLock.Release();
        }
    }

    public async Task SetPreviewAsync(int previewInputId, CancellationToken ct = default)
    {
        ValidateInputId(previewInputId);
        await _cmdLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await SendCommandAsync(BuildCommandString("CPvI", 0, previewInputId), ct).ConfigureAwait(false);
        }
        finally
        {
            _cmdLock.Release();
        }
    }

    public async Task SetAuxAsync(int auxChannel, int inputId, CancellationToken ct = default)
    {
        await _cmdLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await SendCommandAsync(BuildCommandString("CAuS", auxChannel, inputId), ct).ConfigureAwait(false);
        }
        finally
        {
            _cmdLock.Release();
        }
    }

    public Task<List<AtemMacro>> ListMacrosAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new List<AtemMacro>());
    }

    public async Task RunMacroAsync(int macroId, CancellationToken ct = default)
    {
        await _cmdLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await SendCommandAsync($"MAct:{macroId}:0", ct).ConfigureAwait(false);
        }
        finally
        {
            _cmdLock.Release();
        }
    }

    private async Task PerformHandshakeAsync(CancellationToken ct)
    {
        _handshakeTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var hello = BuildConnectHello();
        _logger.LogDebug("Sending ATEM hello: {Hex}", Convert.ToHexString(hello));

        await SendRawAsync(hello, ct).ConfigureAwait(false);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));

        var completed = await Task.WhenAny(_handshakeTcs.Task, Task.Delay(Timeout.InfiniteTimeSpan, timeoutCts.Token))
            .ConfigureAwait(false);

        if (completed != _handshakeTcs.Task)
            throw new TimeoutException("ATEM handshake timed out.");

        await _handshakeTcs.Task.ConfigureAwait(false);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                UdpReceiveResult result;

                try
                {
                    result = await _udpClient!.ReceiveAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }

                var data = result.Buffer;
                if (data.Length < HeaderSize)
                    continue;

                _lastReceivedAt = DateTimeOffset.UtcNow;

                await HandleInboundPacketAsync(data, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ATEM receive loop faulted.");
            SetState(DeviceConnectionState.Faulted);
        }
    }

    private async Task HandleInboundPacketAsync(byte[] data, CancellationToken ct)
    {
        _logger.LogDebug("ATEM RX ({Length}): {Hex}", data.Length, Convert.ToHexString(data));

        // Initial handshake response: your trace shows a 20-byte hello-style frame. :contentReference[oaicite:3]{index=3}
        if (!_isHandshakeComplete && IsHandshakeResponse(data))
        {
            _sessionId = ReadUInt16BE(data, 2);
            _lastReceivedPacketId = ReadUInt16BE(data, 10);

            var ack = BuildPureAck(_sessionId, (ushort)_lastReceivedPacketId);
            _logger.LogDebug("ATEM handshake response session=0x{SessionId:X4}, packet={PacketId}", _sessionId, _lastReceivedPacketId);

            await SendRawAsync(ack, ct).ConfigureAwait(false);

            _isHandshakeComplete = true;
            _handshakeTcs?.TrySetResult(true);
            return;
        }

        var flags = GetFlags(data);
        var sessionId = ReadUInt16BE(data, 2);
        var remotePacketId = ReadUInt16BE(data, 10);

        if (sessionId != 0)
            _sessionId = sessionId;

        // ACK replies from ATEM for our outbound command packets.
        if ((flags & 0x10) != 0)
        {
            var ackedPacketId = ReadUInt16BE(data, 4);
            if (_inFlight.TryRemove(ackedPacketId, out var pending))
            {
                pending.Response.TrySetResult(new DeviceResponse
                {
                    Success = true,
                    StatusCode = 200,
                    Message = "ACK"
                });
            }

            return;
        }

        // Non-ACK inbound packets must be ACKed immediately. Your trace showed ATEM continuously
        // advancing packet ids and expecting ACKs for each one. :contentReference[oaicite:4]{index=4}
        _lastReceivedPacketId = remotePacketId;

        var replyAck = BuildPureAck(_sessionId, (ushort)remotePacketId);
        await SendRawAsync(replyAck, ct).ConfigureAwait(false);

        // No payload beyond the 12-byte header
        if (data.Length <= HeaderSize)
            return;

        ParseStateBlocks(data);
    }

    private class AtemReturnString
    {
        public string Response { get; set; }
    }

    private void ParseStateBlocks(byte[] data)
    {
        int offset = HeaderSize;
        bool anyUpdate = false;

        while (offset + 8 <= data.Length)
        {
            int blockLen = ReadUInt16BE(data, offset);
            if (blockLen < 8 || offset + blockLen > data.Length)
                break;

            var rawName = Encoding.ASCII.GetString(data, offset + 4, 4);
            var name = rawName.TrimEnd('\0');
            var blockData = data.AsSpan(offset + 8, blockLen - 8);
            
            // Format for the ATEM state blocks:
            /*
             *offset 0, length 2   = InputId
             *offset 2, length 20  = LongName
             *offset 22, length 4  = ShortName
             */

            if (name == "InPr")
            {
                var inputID = blockData.Slice(0, 2).ToArray();
                var longName = Encoding.ASCII.GetString(blockData.Slice(2, 20).ToArray());
                var shortName = Encoding.ASCII.GetString(blockData.Slice(22, 4).ToArray());
                
                Console.WriteLine($"Input {inputID} - {longName} ({shortName})");
            }
            
            
            _logger.LogDebug("ATEM block {Name} (raw='{RawName}') len={Len}", name, rawName, blockLen);
            _logger.LogDebug("ATEM block data: {Name} - {BlockData}", name, Convert.ToHexString(blockData));
            _snapshot.Apply(name, blockData);
            anyUpdate = true;

            offset += blockLen;
        }

        if (anyUpdate)
        {
            var state = _snapshot.ToAtemState();
            if (state != null)
                StateChanged?.Invoke(this, state);
        }
    }

    private async Task SendLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var cmd in _sendChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    int packetId;
                    lock (_stateLock)
                    {
                        packetId = _nextSendPacketId++;
                        if (_nextSendPacketId >= MaxPacketId)
                            _nextSendPacketId = 0;
                    }

                    var datagram = BuildCommandDatagram(cmd.Command, packetId);
                    _inFlight[packetId] = new PendingPacket(datagram, cmd.Response, 0, DateTimeOffset.UtcNow);

                    await SendRawAsync(datagram, ct).ConfigureAwait(false);

                    _logger.LogDebug("ATEM TX seq={Seq} cmd='{Command}' hex={Hex}",
                        packetId, cmd.Command, Convert.ToHexString(datagram));
                }
                catch (Exception ex)
                {
                    cmd.Response.TrySetException(ex);
                    _logger.LogError(ex, "Failed sending ATEM command '{Command}'", cmd.Command);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
    }

    private async Task KeepAliveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(KeepAliveIntervalMs, ct).ConfigureAwait(false);

                if (!IsConnected || !_isHandshakeComplete || _sessionId == 0)
                    continue;

                var ack = BuildPureAck(_sessionId, (ushort)_lastReceivedPacketId);
                await SendRawAsync(ack, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
    }

    private async Task RetransmitLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(AckTimeoutMs / 2, ct).ConfigureAwait(false);

                if (!IsConnected)
                    continue;

                var now = DateTimeOffset.UtcNow;

                foreach (var kvp in _inFlight.ToArray())
                {
                    var packetId = kvp.Key;
                    var pending = kvp.Value;

                    if ((now - pending.SentAt).TotalMilliseconds < AckTimeoutMs)
                        continue;

                    if (pending.Attempts >= MaxRetries)
                    {
                        if (_inFlight.TryRemove(packetId, out var failed))
                        {
                            failed.Response.TrySetException(
                                new TimeoutException($"ATEM packet {packetId} was not ACKed after {MaxRetries} retries."));
                        }

                        _logger.LogWarning("ATEM packet {PacketId} exceeded max retransmits", packetId);
                        SetState(DeviceConnectionState.Faulted);
                        continue;
                    }

                    var updated = pending with
                    {
                        Attempts = pending.Attempts + 1,
                        SentAt = now
                    };

                    _inFlight[packetId] = updated;

                    await SendRawAsync(updated.Datagram, ct).ConfigureAwait(false);
                    _logger.LogDebug("Retransmitted ATEM packet {PacketId}, attempt {Attempt}", packetId, updated.Attempts);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
    }

    private async Task SendRawAsync(byte[] datagram, CancellationToken ct)
    {
        if (_udpClient == null)
            throw new InvalidOperationException("ATEM UDP client is not initialized.");

        await _udpClient.SendAsync(datagram, datagram.Length).WaitAsync(ct).ConfigureAwait(false);
    }

    private static bool IsHandshakeResponse(byte[] data)
    {
        return data.Length == HelloPacketLength &&
               data[0] == 0x10 &&
               data[1] == 0x14;
    }

    private byte[] BuildCommandDatagram(string command, int packetId)
    {
        var block = BuildCommandBlockFromString(command);
        var packet = new byte[HeaderSize + block.Length];

        // Important: bytes [4-5] remain zero for outbound command packets.
        WriteHeader(
            packet,
            firstByteFlags: FlagAckRequest,
            sessionId: _sessionId,
            ackId: 0,
            packetId: (ushort)packetId,
            payloadLength: block.Length);

        Array.Copy(block, 0, packet, HeaderSize, block.Length);
        return packet;
    }

    private static byte[] BuildConnectHello()
    {
        // This matches the successful live hello/response flow you captured.
        return new byte[]
        {
            0x10, 0x14, 0x53, 0xAB,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x3A,
            0x00, 0x00,
            0x01,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        };
    }

    private static byte[] BuildPureAck(ushort sessionId, ushort remotePacketId)
    {
        var ack = new byte[HeaderSize];
        WriteHeader(
            ack,
            firstByteFlags: FlagAckReply,
            sessionId: sessionId,
            ackId: remotePacketId,
            packetId: 0,
            payloadLength: 0);
        return ack;
    }

    private static byte[] BuildCommandBlockFromString(string command)
    {
        var parts = command.Split(':');
        var name = parts[0];

        int a0 = parts.Length > 1 && int.TryParse(parts[1], out var v1) ? v1 : 0;
        int a1 = parts.Length > 2 && int.TryParse(parts[2], out var v2) ? v2 : 0;

        byte[] data = name switch
        {
            "CPgI" => new byte[] { (byte)a0, 0, (byte)(a1 >> 8), (byte)(a1 & 0xFF) },
            "CPvI" => new byte[] { (byte)a0, 0, (byte)(a1 >> 8), (byte)(a1 & 0xFF) },
            "DCut" => new byte[] { (byte)a0, 0, 0, 0 },
            "DAut" => new byte[] { (byte)a0, 0, 0, 0 },
            "CAuS" => new byte[] { (byte)a0, 0, (byte)(a1 >> 8), (byte)(a1 & 0xFF) },
            "MAct" => new byte[] { (byte)(a0 >> 8), (byte)(a0 & 0xFF), (byte)a1, 0 },
            "CTTp" => new byte[] { (byte)a0, 0, (byte)a1, 0 },
            "CTMx" => new byte[] { (byte)a0, 0, (byte)(a1 >> 8), (byte)(a1 & 0xFF) },
            _ => new byte[] { 0, 0, 0, 0 }
        };

        return BuildCommandBlock(name, data);
    }

    private static byte[] BuildCommandBlock(string name, byte[] data)
    {
        int blockLen = 8 + data.Length;
        var block = new byte[blockLen];

        block[0] = (byte)(blockLen >> 8);
        block[1] = (byte)(blockLen & 0xFF);
        block[2] = 0;
        block[3] = 0;

        var nameBytes = Encoding.ASCII.GetBytes(name);
        Array.Copy(nameBytes, 0, block, 4, Math.Min(4, nameBytes.Length));
        Array.Copy(data, 0, block, 8, data.Length);

        return block;
    }

    private static void WriteHeader(
        byte[] buffer,
        byte firstByteFlags,
        ushort sessionId,
        ushort ackId,
        ushort packetId,
        int payloadLength)
    {
        int totalLength = HeaderSize + payloadLength;

        buffer[0] = (byte)(firstByteFlags | (totalLength >> 8));
        buffer[1] = (byte)(totalLength & 0xFF);
        buffer[2] = (byte)(sessionId >> 8);
        buffer[3] = (byte)(sessionId & 0xFF);
        buffer[4] = (byte)(ackId >> 8);
        buffer[5] = (byte)(ackId & 0xFF);
        buffer[6] = 0;
        buffer[7] = 0;
        buffer[8] = 0;
        buffer[9] = 0;
        buffer[10] = (byte)(packetId >> 8);
        buffer[11] = (byte)(packetId & 0xFF);
    }

    private static ushort ReadUInt16BE(byte[] data, int offset)
        => (ushort)((data[offset] << 8) | data[offset + 1]);

    private static byte GetFlags(byte[] data) => (byte)(data[0] >> 3);

    private static string BuildCommandString(string name, params int[] args)
        => args.Length == 0 ? name : $"{name}:{string.Join(':', args)}";

    private static void ValidateInputId(int inputId)
    {
        if (inputId < 1)
            throw new ArgumentOutOfRangeException(nameof(inputId), "Input ID must be >= 1.");
    }

    private void SetState(DeviceConnectionState newState)
    {
        var old = _state;
        _state = newState;
        if (old != newState)
            ConnectionStateChanged?.Invoke(this, ConnectionState);
    }

    private void ResetProtocolState()
    {
        _sessionId = 0;
        _lastReceivedPacketId = 0;
        _nextSendPacketId = 1;
        _isHandshakeComplete = false;
        _lastReceivedAt = DateTimeOffset.MinValue;
        _inFlight.Clear();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AtemUdpConnection));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        await DisconnectAsync().ConfigureAwait(false);

        _cmdLock.Dispose();
    }

    private enum DeviceConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Faulted
    }

    private sealed record OutboundCommand(
        string Command,
        TaskCompletionSource<DeviceResponse> Response);

    private sealed record PendingPacket(
        byte[] Datagram,
        TaskCompletionSource<DeviceResponse> Response,
        int Attempts,
        DateTimeOffset SentAt);
}