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

    /// <summary>Temporary session ID used in the client Hello packet (placeholder until ATEM assigns a real one).</summary>
    private const ushort HelloSessionId = 0x70BF;

    // Packet flags (stored in the upper bits of byte[0] of the header).
    private const byte FlagAck        = 0x80;  // Pure ACK packet (no payload)
    private const byte FlagHello      = 0x10;  // Handshake packet (hello / init)
    private const byte FlagAckRequest = 0x08;  // Requesting an ACK for this packet
    private const byte FlagRetransmit = 0x04;  // This is a retransmit

    // ATEM handshake connection codes (byte[12] in 20-byte hello packets).
    private const byte HelloConnSyn    = 0x01;  // Client SYN (hello)
    private const byte HelloConnSynAck = 0x02;  // Server SYN-ACK (echoes temp session ID)
    private const byte HelloConnAck    = 0x03;  // Client ACK (acknowledges SYN-ACK)
    private const byte HelloConnInit   = 0x04;  // Server INIT (assigns real session ID)

    // Header size in bytes.
    private const int HeaderSize = 12;

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly AtemStateSnapshot _snapshot = new();

    // External command lock so multi-step operations (e.g. FadeToProgram) are atomic.
    private readonly SemaphoreSlim _cmdLock = new(1, 1);

    // Cancelled by HandleHandshakeIntermediatePacketAsync as soon as the first SYN-ACK
    // is received so that SendHandshakeAsync stops retransmitting SYN packets.  A new
    // source is created each time SendHandshakeAsync is entered (i.e. each connect /
    // reconnect attempt).  Marked volatile so the write in SendHandshakeAsync and the
    // read in HandleHandshakeIntermediatePacketAsync (running on different threads) are
    // always consistent.
    private volatile CancellationTokenSource? _synLoopCts;

    // ── IAtemConnection ───────────────────────────────────────────────────────
    public AtemConnectionState ConnectionState => State switch
    {
        DeviceConnectionState.Connected  => AtemConnectionState.Connected,
        DeviceConnectionState.Connecting => AtemConnectionState.Connecting,
        DeviceConnectionState.Faulted    => AtemConnectionState.Degraded,
        _                                => AtemConnectionState.Disconnected
    };

    public AtemState? CurrentState => _snapshot.ToAtemState();
    public event EventHandler<AtemConnectionState>? ConnectionStateChanged;
    public event EventHandler<AtemState>? StateChanged;

    /// <summary>
    /// Waits until at least one <c>PrgI</c> state update has been received from the ATEM
    /// switcher (i.e. program-input state is initialised), or until <paramref name="timeout"/>
    /// elapses or <paramref name="ct"/> is cancelled.
    /// </summary>
    /// <returns>
    /// <c>true</c> if program-input state is available; <c>false</c> if the timeout expired.
    /// </returns>
    public Task<bool> WaitForProgramInputAsync(TimeSpan timeout, CancellationToken ct = default)
        => _snapshot.WaitForProgramInputAsync(timeout, ct);

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an ATEM UDP connection to the specified host.
    /// </summary>
    public AtemUdpConnection(string host, ILogger<AtemUdpConnection> logger, int port = DefaultAtemPort)
        : base(host, port, logger) { }

    // ── Base-class overrides ──────────────────────────────────────────────────
    protected override string DeviceTypeName => "ATEM";
    protected override bool RequiresHandshake => true;
    protected override bool UsesReliability => true;
    protected override bool AckResolvesResponse => true;
    protected override bool RequiresKeepAlive => true;
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
        var hello = BuildHandshakePacket();

        // Create a per-attempt CTS so HandleHandshakeIntermediatePacketAsync can stop the
        // SYN loop the moment the first SYN-ACK arrives.  Without this the ATEM receives a
        // fresh SYN ~250 ms after the client ACK and resets its handshake state, causing an
        // infinite SYN ↔ SYN-ACK loop until the handshake timeout fires.
        using var synCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _synLoopCts = synCts;
        try
        {
            while (!synCts.Token.IsCancellationRequested)
            {
                await SendRawDatagramAsync(hello, synCts.Token);

                try { await Task.Delay(250, synCts.Token); }
                catch (OperationCanceledException) { break; }
            }
        }
        finally
        {
            _synLoopCts = null;
        }
    }

    protected override bool IsHandshakeResponse(ReceivedDatagram rx)
    {
        if (rx.Data.Length != 20) return false;
        if ((rx.Data[0] & FlagHello) == 0) return false;

        // The ATEM INIT packet carries connection code 0x04 and a newly-assigned session ID
        // (different from our placeholder HelloSessionId).
        var sessionId = (ushort)((rx.Data[2] << 8) | rx.Data[3]);
        return rx.Data[12] == HelloConnInit && sessionId != HelloSessionId;
    }

    protected override void ApplyHandshakeResponse(ReceivedDatagram rx)
    {
        // Extract the server-assigned session ID from bytes [2-3].
        ProtocolContext.SessionId = (ushort)((rx.Data[2] << 8) | rx.Data[3]);
        // bytes [10-11] carry the server's packet ID for the INIT frame.
        ProtocolContext.LastReceivedSequence = (rx.Data[10] << 8) | rx.Data[11];

        Logger.LogInformation("ATEM session established – session ID 0x{SessionId:X4}", ProtocolContext.SessionId);
    }

    protected override async Task<bool> HandleHandshakeIntermediatePacketAsync(ReceivedDatagram rx, CancellationToken ct)
    {
        // Detect ATEM SYN-ACK (server's echo of our hello with connection code 0x02).
        // Respond with a client ACK (code 0x03) so the ATEM proceeds to send the INIT frame.
        if (rx.Data.Length == 20 && (rx.Data[0] & FlagHello) != 0 && rx.Data[12] == HelloConnSynAck)
        {
            Logger.LogDebug("ATEM handshake: received SYN-ACK, sending client ACK");

            // Stop the SYN send loop immediately.  If we keep sending SYN the ATEM
            // treats each one as a fresh connection attempt and replies with another
            // SYN-ACK instead of advancing to the INIT packet.
            _synLoopCts.Cancel();
            Logger.LogDebug("Ending loop now!");
            
            Logger.LogDebug("Building Handshake packet");
            var ack = BuildHandshakePacket();
            
            Logger.LogDebug("Sending datagram with build Handshake: {HandshakePacket}", BitConverter.ToString(ack));
            await SendRawDatagramAsync(ack, ct);
            return true;
        }

        return false;
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
        // Do NOT update LastReceivedSequence from ACK packets.
        // ACK packets' [4-5] is "which packet id is being acknowledged" (ours),
        // not "the last remote packet we saw".
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

        // New datagram builder matching a known working imlementation of ATEM Software Control
        WriteHeader(packet, FlagAckRequest, ctx.SessionId,
            ackId: 0, // acks should be handled separately as dedicated 12-byte packets
            packetId: (ushort)ctx.OutboundSequence,
            payloadLength: cmdBlock.Length);
        
        Array.Copy(cmdBlock, 0, packet, HeaderSize, cmdBlock.Length);
        return packet;
    }

    // ── State parsing ─────────────────────────────────────────────────────────
    protected override bool TryParseDeviceResponse(ReceivedDatagram rx, out DeviceResponse response)
    {
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
    
    // ── Handshake packet builder ──────────────────────────────────────────────

    /// <summary>
    /// Builds a 20-byte ATEM handshake (hello) packet
    /// Known working hello packet from wireshark capture - same each time.
    /// </summary>
    private static byte[] BuildHandshakePacket()
    {
        var helloPkt = new byte[]
        {
            0x10, 0x14, 0x53, 0xAB,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x3A,
            0x00, 0x00,
            0x01,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        };
        return helloPkt;
    }
}
