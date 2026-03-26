using Microsoft.Extensions.Logging;
using ProdControlAV.Agent.Interfaces;
using ProdControlAV.Agent.Models;
using System.Text;

namespace ProdControlAV.Agent.Services;

/// <summary>
/// Native ATEM UDP connection that implements the Blackmagic Design ATEM binary protocol
/// on top of <see cref="BaseUdpDeviceConnection"/>.
///
/// Packet-header layout (12 bytes, big-endian):
/// <code>
/// [0]   flags (bits 7-3) | length-high (bits 2-0)
/// [1]   length-low
/// [2-3] session ID
/// [4-5] ACK ID (remote sequence number being ACKed)
/// [6-9] reserved (0x00)
/// [10-11] local packet ID (our outbound sequence)
/// </code>
///
/// Each command embedded in a packet payload uses an 8-byte block header:
/// <code>
/// [0-1] block total length (uint16-be, includes this 8-byte header)
/// [2-3] 0x0000
/// [4-7] command name (4 ASCII bytes)
/// [8+]  command data
/// </code>
///
/// The command strings passed to <see cref="BaseUdpDeviceConnection.SendCommandAsync"/>
/// use the format <c>"NAME:arg0:arg1:..."</c>, e.g. <c>"CPgI:0:1"</c>.
/// </summary>
public sealed class AtemUdpConnection : BaseUdpDeviceConnection, IAtemConnection
{
    // ── ATEM constants ────────────────────────────────────────────────────────

    /// <summary>Default ATEM control port.</summary>
    public const int DefaultAtemPort = 9910;

    /// <summary>Temporary session ID used in the client Hello packet.</summary>
    private const ushort HelloSessionId = 0x1337;

    // Packet flags (stored in the top 5 bits of byte[0] of the header).
    private const byte FlagAck        = 0x80;  // This is an ACK packet (no payload)
    private const byte FlagHello      = 0x10;  // Client hello / handshake init
    private const byte FlagAckRequest = 0x08;  // Please ACK this packet
    private const byte FlagRetransmit = 0x04;  // This is a retransmit
    private const byte FlagInit       = 0x02;  // Server init response to Hello

    // Header size in bytes.
    private const int HeaderSize = 12;

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly AtemStateSnapshot _snapshot = new();

    // External command lock so multi-step operations (e.g. FadeToProgram) are atomic.
    private readonly SemaphoreSlim _cmdLock = new(1, 1);

    // ── IAtemConnection ───────────────────────────────────────────────────────

    /// <inheritdoc/>
    public AtemConnectionState ConnectionState => State switch
    {
        DeviceConnectionState.Connected  => AtemConnectionState.Connected,
        DeviceConnectionState.Connecting => AtemConnectionState.Connecting,
        DeviceConnectionState.Faulted    => AtemConnectionState.Degraded,
        _                                => AtemConnectionState.Disconnected
    };

    /// <inheritdoc/>
    public AtemState? CurrentState => _snapshot.ToAtemState();

    /// <inheritdoc/>
    public event EventHandler<AtemConnectionState>? ConnectionStateChanged;

    /// <inheritdoc/>
    public event EventHandler<AtemState>? StateChanged;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an ATEM UDP connection to the specified host.
    /// </summary>
    /// <param name="host">IP address or hostname of the ATEM switcher.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="port">UDP port (default 9910).</param>
    public AtemUdpConnection(string host, ILogger<AtemUdpConnection> logger, int port = DefaultAtemPort)
        : base(host, port, logger) { }

    // ── Base-class overrides ──────────────────────────────────────────────────

    protected override string DeviceTypeName => "ATEM";

    // The ATEM protocol uses a handshake to establish a session ID.
    protected override bool RequiresHandshake => true;

    // The ATEM protocol uses per-packet ACKs for reliability.
    protected override bool UsesReliability => true;

    // For ATEM, receiving the ACK for a command packet means the command was accepted.
    protected override bool AckResolvesResponse => true;

    // ATEM requires a periodic keepalive (ACK ping) to stay connected.
    protected override bool RequiresKeepAlive => true;

    // Send keepalives every 250 ms to stay within the ATEM's ~2-second disconnect threshold.
    protected override TimeSpan KeepAliveInterval => TimeSpan.FromMilliseconds(250);

    protected override TimeSpan AckTimeout => TimeSpan.FromMilliseconds(500);
    protected override int MaxRetries => 5;

    // ── IAtemConnection: connect / disconnect ─────────────────────────────────

    /// <summary>
    /// Connects to the ATEM switcher by performing the UDP handshake.
    /// Delegates to <see cref="BaseUdpDeviceConnection.StartAsync"/>.
    /// </summary>
    public Task ConnectAsync(CancellationToken ct = default) => StartAsync(ct);

    /// <summary>
    /// Disconnects from the ATEM switcher.
    /// Delegates to <see cref="BaseUdpDeviceConnection.DisconnectAsync"/>.
    /// </summary>
    public new Task DisconnectAsync() => base.DisconnectAsync();

    // ── IAtemConnection: high-level commands ──────────────────────────────────

    /// <inheritdoc/>
    public async Task CutToProgramAsync(int programInputId, CancellationToken ct = default)
    {
        ValidateInputId(programInputId);
        await _cmdLock.WaitAsync(ct);
        try
        {
            EnsureConnected();
            // Directly change the program bus without going via preview.
            await SendCommandAsync(BuildCommandString("CPgI", 0, programInputId), ct);
        }
        finally
        {
            _cmdLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task FadeToProgramAsync(int programInputId, int? transitionRate = null, CancellationToken ct = default)
    {
        ValidateInputId(programInputId);
        await _cmdLock.WaitAsync(ct);
        try
        {
            EnsureConnected();
            const int me = 0;
            // Set preview to the target input.
            await SendCommandAsync(BuildCommandString("CPvI", me, programInputId), ct);
            // Set transition type to mix (0).
            await SendCommandAsync(BuildCommandString("CTTp", me, 0), ct);
            // If a rate was supplied, update the mix rate.
            if (transitionRate.HasValue)
                await SendCommandAsync(BuildCommandString("CTMx", me, transitionRate.Value), ct);
            // Execute the auto transition.
            await SendCommandAsync(BuildCommandString("DAut", me), ct);
        }
        finally
        {
            _cmdLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task SetPreviewAsync(int previewInputId, CancellationToken ct = default)
    {
        ValidateInputId(previewInputId);
        await _cmdLock.WaitAsync(ct);
        try
        {
            EnsureConnected();
            await SendCommandAsync(BuildCommandString("CPvI", 0, previewInputId), ct);
        }
        finally
        {
            _cmdLock.Release();
        }
    }

    /// <summary>
    /// Routes an auxiliary (AUX) output to the specified input source.
    /// </summary>
    /// <param name="auxChannel">Zero-based AUX channel index.</param>
    /// <param name="inputId">Source input ID.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task SetAuxAsync(int auxChannel, int inputId, CancellationToken ct = default)
    {
        await _cmdLock.WaitAsync(ct);
        try
        {
            EnsureConnected();
            await SendCommandAsync(BuildCommandString("CAuS", auxChannel, inputId), ct);
        }
        finally
        {
            _cmdLock.Release();
        }
    }

    /// <inheritdoc/>
    public Task<List<AtemMacro>> ListMacrosAsync(CancellationToken ct = default)
    {
        EnsureConnected();
        // State-cache approach: macro metadata is populated from "MRPr" / "MacP" packets
        // received during connection.  Return an empty list until cache is populated.
        return Task.FromResult(new List<AtemMacro>());
    }

    /// <inheritdoc/>
    public async Task RunMacroAsync(int macroId, CancellationToken ct = default)
    {
        await _cmdLock.WaitAsync(ct);
        try
        {
            EnsureConnected();
            // MAct: [macroIdH, macroIdL, action=0(Run), 0x00]
            await SendCommandAsync($"MAct:{macroId}:0", ct);
        }
        finally
        {
            _cmdLock.Release();
        }
    }

    // ── Handshake ─────────────────────────────────────────────────────────────
    protected override async Task SendHandshakeAsync(CancellationToken ct)
    {
        // Known-good ATEM Software Control hello for ATEM Television Studio (captured on your network):
        // 10 14 70 bf 00 00 00 00 00 9e 00 00 01 00 00 00 00 00 00 00
        var hello = Convert.FromHexString("101470bf00000000009e00000100000000000000");

        // (Optional but recommended) retry hello during the handshake window
        while (!ct.IsCancellationRequested)
        {
            await SendRawDatagramAsync(hello, ct);

            try { await Task.Delay(250, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    protected override bool IsHandshakeResponse(ReceivedDatagram rx)
    {
        LogAtemHeaderDebug(rx);
        if (rx.Data.Length == 20 && (rx.Data[0] & 0xF8) == 0x30)
            return true;

        return false;
        // Server's Init response has the INIT flag (0x02) set in byte[0].
        // return rx.Data.Length >= HeaderSize && (rx.Data[0] & FlagInit) != 0;
    }

    protected override void ApplyHandshakeResponse(ReceivedDatagram rx)
    {
        // Extract the server-assigned session ID from bytes [2-3].
        ProtocolContext.SessionId = (ushort)((rx.Data[2] << 8) | rx.Data[3]);
        // Remember the server's packet ID so we can ACK it.
        ProtocolContext.LastReceivedSequence = (rx.Data[10] << 8) | rx.Data[11];

        Logger.LogInformation("ATEM session established – session ID 0x{SessionId:X4}", ProtocolContext.SessionId);
    }

    // ── Keepalive (ACK ping) ──────────────────────────────────────────────────

    protected override async Task SendKeepAliveAsync(CancellationToken ct)
    {
        // An empty ACK packet with the last-seen remote packet ID keeps the session alive.
        var ack = BuildPureAck((ushort)ProtocolContext.LastReceivedSequence);
        await SendRawDatagramAsync(ack, ct);
    }

    // Pure ACK packets have no payload and no local packet ID.
    protected override bool IsKeepAliveResponse(ReceivedDatagram rx) => false;

    // ── Reliability (ACK) ─────────────────────────────────────────────────────

    protected override bool IsAckDatagram(ReceivedDatagram rx)
    {
        if (rx.Data.Length < HeaderSize) return false;
        // Pure ACK: only the ACK flag set and no payload.
        return (rx.Data[0] & FlagAck) != 0 && rx.Data.Length == HeaderSize;
    }

    protected override byte[] BuildAckDatagram(UdpProtocolContext ctx, ReceivedDatagram rx)
    {
        var remoteSeq = (ushort)((rx.Data[10] << 8) | rx.Data[11]);
        return BuildPureAck(remoteSeq);
    }

    protected override void ApplyAck(ReceivedDatagram rx)
    {
        // Update the last-received sequence so keepalives can ACK it correctly.
        if (rx.Data.Length >= HeaderSize)
            ProtocolContext.LastReceivedSequence = (rx.Data[4] << 8) | rx.Data[5];
    }

    protected override int GetDatagramSequence(ReceivedDatagram rx)
    {
        if (rx.Data.Length < HeaderSize) return 0;
        return (rx.Data[10] << 8) | rx.Data[11];
    }

    protected override int GetAckedSequence(ReceivedDatagram rx)
    {
        if (rx.Data.Length < HeaderSize) return 0;
        // The ACK ID field (bytes [4-5]) contains the local packet ID being ACKed.
        return (rx.Data[4] << 8) | rx.Data[5];
    }

    // ── Command encoding ──────────────────────────────────────────────────────

    protected override byte[] BuildDatagramFromCommand(string command, UdpProtocolContext ctx)
    {
        var cmdBlock = BuildCommandBlockFromString(command);

        var packet = new byte[HeaderSize + cmdBlock.Length];
        WriteHeader(packet, FlagAckRequest, ctx.SessionId,
            ackId: (ushort)ctx.LastReceivedSequence,
            packetId: (ushort)ctx.OutboundSequence,
            payloadLength: cmdBlock.Length);
        Array.Copy(cmdBlock, 0, packet, HeaderSize, cmdBlock.Length);
        return packet;
    }

    // ── State parsing ─────────────────────────────────────────────────────────

    protected override bool TryParseDeviceResponse(ReceivedDatagram rx, out DeviceResponse response)
    {
        LogAtemHeaderDebug(rx);
        response = default!;

        if (rx.Data.Length < HeaderSize) return false;

        // Update LastReceivedSequence from every non-ACK inbound packet.
        ProtocolContext.LastReceivedSequence = (rx.Data[10] << 8) | rx.Data[11];

        // Parse embedded command blocks and update the state snapshot.
        bool anyUpdate = false;
        int offset = HeaderSize;
        while (offset + 8 <= rx.Data.Length)
        {
            int blockLen = (rx.Data[offset] << 8) | rx.Data[offset + 1];
            if (blockLen < 8 || offset + blockLen > rx.Data.Length) break;

            var name = Encoding.ASCII.GetString(rx.Data, offset + 4, 4);
            var dataSpan = rx.Data.AsSpan(offset + 8, blockLen - 8);
            _snapshot.Apply(name, dataSpan);
            anyUpdate = true;

            offset += blockLen;
        }

        if (anyUpdate)
        {
            var state = _snapshot.ToAtemState();
            if (state != null)
                StateChanged?.Invoke(this, state);
        }

        // ATEM uses ACKs (not payload responses) to confirm commands.
        // State updates are always unsolicited – return false so the base class
        // does not try to deliver them as command responses.
        return false;
    }

    // ── State-change notifications ────────────────────────────────────────────

    protected override void OnDeviceStateChanged(DeviceConnectionState newState)
    {
        ConnectionStateChanged?.Invoke(this, ConnectionState);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void EnsureConnected()
    {
        if (!IsConnected)
            throw new InvalidOperationException(
                $"ATEM is not connected (state: {ConnectionState}). Cannot execute command.");
    }

    private static void ValidateInputId(int inputId)
    {
        if (inputId < 1)
            throw new ArgumentOutOfRangeException(nameof(inputId), "Input ID must be >= 1.");
    }

    /// <summary>
    /// Builds a short "NAME:a0:a1" command string accepted by
    /// <see cref="BuildDatagramFromCommand"/>.
    /// </summary>
    private static string BuildCommandString(string name, params int[] args)
        => args.Length == 0 ? name : $"{name}:{string.Join(':', args)}";

    /// <summary>
    /// Parses a "NAME:arg0:arg1:..." string into a binary ATEM command block.
    /// </summary>
    private static byte[] BuildCommandBlockFromString(string command)
    {
        var parts = command.Split(':');
        var name  = parts[0];

        // Most ATEM commands carry 4 bytes of data.
        // Parse each colon-delimited arg; missing or invalid args default to 0.
        int a0 = parts.Length > 1 && int.TryParse(parts[1], out var v1) ? v1 : 0;
        int a1 = parts.Length > 2 && int.TryParse(parts[2], out var v2) ? v2 : 0;

        byte[] data = name switch
        {
            // Change Program Input:  [ME, 0x00, inputH, inputL]
            "CPgI" => new byte[] { (byte)a0, 0, (byte)(a1 >> 8), (byte)(a1 & 0xFF) },
            // Change Preview Input:  [ME, 0x00, inputH, inputL]
            "CPvI" => new byte[] { (byte)a0, 0, (byte)(a1 >> 8), (byte)(a1 & 0xFF) },
            // Do Cut:                [ME, 0x00, 0x00, 0x00]
            "DCut" => new byte[] { (byte)a0, 0, 0, 0 },
            // Do Auto:               [ME, 0x00, 0x00, 0x00]
            "DAut" => new byte[] { (byte)a0, 0, 0, 0 },
            // Change Aux Source:     [channel, 0x00, srcH, srcL]
            "CAuS" => new byte[] { (byte)a0, 0, (byte)(a1 >> 8), (byte)(a1 & 0xFF) },
            // Macro Action:          [macroIdH, macroIdL, action, 0x00]
            "MAct" => new byte[] { (byte)(a0 >> 8), (byte)(a0 & 0xFF), (byte)a1, 0 },
            // Change Transition Type:[ME, 0x00, type, 0x00]
            "CTTp" => new byte[] { (byte)a0, 0, (byte)a1, 0 },
            // Change Transition Mix rate: [ME, 0x00, rateH, rateL]
            "CTMx" => new byte[] { (byte)a0, 0, (byte)(a1 >> 8), (byte)(a1 & 0xFF) },
            // Unknown command – send 4 zero bytes so the header is still valid.
            _      => new byte[] { 0, 0, 0, 0 }
        };

        return BuildCommandBlock(name, data);
    }

    /// <summary>
    /// Builds a raw ATEM command block (8-byte header + data).
    /// </summary>
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

    /// <summary>
    /// Writes the 12-byte ATEM packet header into <paramref name="buffer"/> at offset 0.
    /// </summary>
    private static void WriteHeader(
        byte[] buffer, byte flags, ushort sessionId, ushort ackId, ushort packetId, int payloadLength)
    {
        int totalLength = HeaderSize + payloadLength;
        buffer[0]  = (byte)(flags | (totalLength >> 8));
        buffer[1]  = (byte)(totalLength & 0xFF);
        buffer[2]  = (byte)(sessionId >> 8);
        buffer[3]  = (byte)(sessionId & 0xFF);
        buffer[4]  = (byte)(ackId >> 8);
        buffer[5]  = (byte)(ackId & 0xFF);
        buffer[6]  = 0;
        buffer[7]  = 0;
        buffer[8]  = 0;
        buffer[9]  = 0;
        buffer[10] = (byte)(packetId >> 8);
        buffer[11] = (byte)(packetId & 0xFF);
    }

    /// <summary>
    /// Builds a pure ACK packet (12 bytes, no payload) for the given remote sequence ID.
    /// </summary>
    private byte[] BuildPureAck(ushort remoteSeq)
    {
        var ack = new byte[HeaderSize];
        WriteHeader(ack, FlagAck, ProtocolContext.SessionId, ackId: remoteSeq, packetId: 0, payloadLength: 0);
        return ack;
    }
    
    // -- Helpers: Can delete later
    // ---- TEMP DEBUG: ATEM header decode helpers ----
    private static ushort ReadU16BE(byte[] data, int offset)
    {
        if (data.Length < offset + 2) return 0;
        return (ushort)((data[offset] << 8) | data[offset + 1]);
    }

    private void LogAtemHeaderDebug(ReceivedDatagram rx)
    {
        if (!Logger.IsEnabled(LogLevel.Debug))
            return;

        if (rx.Data.Length < 12)
        {
            Logger.LogDebug("ATEM RX too short for header: {Len} bytes. Raw={Raw}",
                rx.Data.Length, BitConverter.ToString(rx.Data));
            return;
        }

        byte b0 = rx.Data[0];
        byte b1 = rx.Data[1];

        // Per comment: [0] flags (bits 7-3) | length-high (bits 2-0)
        int flagsUpper = b0 & 0xF8;   // bits 7..3
        int lenHigh3   = b0 & 0x07;   // bits 2..0

        int declaredLen = (lenHigh3 << 8) | b1;

        ushort sessionId = ReadU16BE(rx.Data, 2);
        ushort ackId     = ReadU16BE(rx.Data, 4);
        ushort packetId  = ReadU16BE(rx.Data, 10);

        Logger.LogDebug(
            "ATEM RX decode: b0=0x{B0:X2} (flagsUpper=0x{Flags:X2}, lenHigh3=0x{LenHigh:X1}), b1=0x{B1:X2}, declaredLen={DeclaredLen}, " +
            "sessionId=0x{SessionId:X4}, ackId=0x{AckId:X4}, packetId=0x{PacketId:X4}, actualLen={ActualLen}, raw={Raw}",
            b0, flagsUpper, lenHigh3, b1, declaredLen,
            sessionId, ackId, packetId, rx.Data.Length, BitConverter.ToString(rx.Data));
    }
}
