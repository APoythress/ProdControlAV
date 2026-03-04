using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ProdControlAV.Agent.Interfaces;
using ProdControlAV.Agent.Services;
using Xunit;

namespace ProdControlAV.Tests;

// ── Helpers ───────────────────────────────────────────────────────────────────

file static class AtemTestHelper
{
    // ATEM header constants (mirrors AtemUdpConnection private constants).
    public const byte FlagAck        = 0x80;
    public const byte FlagHello      = 0x10;
    public const byte FlagAckRequest = 0x08;
    public const byte FlagInit       = 0x02;
    public const int  HeaderSize     = 12;

    /// <summary>Starts a local UDP server on a random free port.</summary>
    public static (UdpClient server, int port) StartServer()
    {
        var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port   = ((IPEndPoint)server.Client.LocalEndPoint!).Port;
        return (server, port);
    }

    /// <summary>Writes the 12-byte ATEM packet header into <paramref name="buf"/> at offset 0.</summary>
    public static void WriteHeader(byte[] buf, byte flags, ushort sessionId, ushort ackId,
                                    ushort packetId, int payloadLength)
    {
        int total = HeaderSize + payloadLength;
        buf[0]  = (byte)(flags | (total >> 8));
        buf[1]  = (byte)(total & 0xFF);
        buf[2]  = (byte)(sessionId >> 8);
        buf[3]  = (byte)(sessionId & 0xFF);
        buf[4]  = (byte)(ackId >> 8);
        buf[5]  = (byte)(ackId & 0xFF);
        buf[10] = (byte)(packetId >> 8);
        buf[11] = (byte)(packetId & 0xFF);
    }

    /// <summary>Builds a pure ACK datagram (12 bytes).</summary>
    public static byte[] BuildAck(ushort sessionId, ushort ackId)
    {
        var pkt = new byte[HeaderSize];
        WriteHeader(pkt, FlagAck, sessionId, ackId, packetId: 0, payloadLength: 0);
        return pkt;
    }

    /// <summary>Builds a server-INIT datagram (handshake response) with a given session ID.</summary>
    public static byte[] BuildInitResponse(ushort assignedSessionId, ushort serverPacketId = 1)
    {
        var pkt = new byte[HeaderSize + 8]; // 8-byte minimal payload
        WriteHeader(pkt, FlagInit, assignedSessionId, ackId: 0, packetId: serverPacketId, payloadLength: 8);
        return pkt;
    }

    /// <summary>Builds an ATEM state-update packet containing a single command block.</summary>
    public static byte[] BuildStatePacket(ushort sessionId, ushort packetId, string cmdName, byte[] cmdData)
    {
        int blockLen = 8 + cmdData.Length;
        var payload  = new byte[blockLen];
        payload[0] = (byte)(blockLen >> 8);
        payload[1] = (byte)(blockLen & 0xFF);
        var nameBytes = Encoding.ASCII.GetBytes(cmdName);
        Array.Copy(nameBytes, 0, payload, 4, Math.Min(4, nameBytes.Length));
        Array.Copy(cmdData, 0, payload, 8, cmdData.Length);

        var pkt = new byte[HeaderSize + blockLen];
        WriteHeader(pkt, FlagAckRequest, sessionId, ackId: 0, packetId: packetId, payloadLength: blockLen);
        Array.Copy(payload, 0, pkt, HeaderSize, blockLen);
        return pkt;
    }
}

// ── AtemStateSnapshot unit tests ─────────────────────────────────────────────

public class AtemStateSnapshotTests
{
    [Fact]
    public void ToAtemState_BeforeAnyApply_ReturnsNull()
    {
        var snapshot = new AtemStateSnapshot();
        Assert.Null(snapshot.ToAtemState());
    }

    [Fact]
    public void Apply_PrgI_UpdatesProgramInput()
    {
        var snapshot = new AtemStateSnapshot();
        // ME=0, input=2 → data: [0x00, 0x00, 0x00, 0x02]
        snapshot.Apply("PrgI", new byte[] { 0, 0, 0, 2 });
        Assert.Equal(2, snapshot.GetProgramInput(me: 0));
    }

    [Fact]
    public void Apply_PrvI_UpdatesPreviewInput()
    {
        var snapshot = new AtemStateSnapshot();
        snapshot.Apply("PrvI", new byte[] { 0, 0, 0, 3 });
        Assert.Equal(3, snapshot.GetPreviewInput(me: 0));
    }

    [Fact]
    public void Apply_AuxS_UpdatesAuxSource()
    {
        var snapshot = new AtemStateSnapshot();
        // channel=0, source=7
        snapshot.Apply("AuxS", new byte[] { 0, 0, 0, 7 });
        Assert.Equal(7, snapshot.GetAuxSource(channel: 0));
    }

    [Fact]
    public void Apply_TrSS_UpdatesInTransition()
    {
        var snapshot = new AtemStateSnapshot();
        snapshot.Apply("PrgI", new byte[] { 0, 0, 0, 1 }); // seed state so ToAtemState returns non-null
        snapshot.Apply("TrSS", new byte[] { 0, 0x01 });      // ME=0, inTransition=true
        Assert.True(snapshot.ToAtemState()!.InTransition);
    }

    [Fact]
    public void Apply_UnknownCommand_IsIgnored()
    {
        var snapshot = new AtemStateSnapshot();
        // Should not throw; unknown commands are silently skipped.
        snapshot.Apply("UNKN", new byte[] { 1, 2, 3, 4 });
        Assert.Null(snapshot.ToAtemState());
    }

    [Fact]
    public void ToAtemState_ReflectsMostRecentUpdates()
    {
        var snapshot = new AtemStateSnapshot();
        snapshot.Apply("PrgI", new byte[] { 0, 0, 0, 1 });
        snapshot.Apply("PrvI", new byte[] { 0, 0, 0, 2 });
        snapshot.Apply("PrgI", new byte[] { 0, 0, 0, 5 }); // overwrite

        var state = snapshot.ToAtemState()!;
        Assert.Equal(5, state.ProgramInputId);
        Assert.Equal(2, state.PreviewInputId);
    }

    [Fact]
    public void Apply_MultipleMe_TrackedSeparately()
    {
        var snapshot = new AtemStateSnapshot();
        snapshot.Apply("PrgI", new byte[] { 0, 0, 0, 1 }); // ME0 → input 1
        snapshot.Apply("PrgI", new byte[] { 1, 0, 0, 2 }); // ME1 → input 2
        Assert.Equal(1, snapshot.GetProgramInput(me: 0));
        Assert.Equal(2, snapshot.GetProgramInput(me: 1));
    }

    [Fact]
    public void Apply_TooShortData_IsIgnored()
    {
        var snapshot = new AtemStateSnapshot();
        // 3 bytes is too short for PrgI (needs 4) – should not throw
        snapshot.Apply("PrgI", new byte[] { 0, 0, 1 });
        Assert.Null(snapshot.ToAtemState());
    }
}

// ── AtemUdpConnection packet-building tests ───────────────────────────────────

public class AtemUdpConnectionPacketTests
{
    [Fact]
    public void Constructor_ValidArgs_IsNotConnected()
    {
        var conn = new AtemUdpConnection("127.0.0.1", NullLogger<AtemUdpConnection>.Instance);
        Assert.False(conn.IsConnected);
        Assert.Equal(AtemConnectionState.Disconnected, conn.ConnectionState);
    }

    [Fact]
    public void Constructor_EmptyHost_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new AtemUdpConnection("", NullLogger<AtemUdpConnection>.Instance));
    }

    [Fact]
    public void Constructor_InvalidPort_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AtemUdpConnection("127.0.0.1", NullLogger<AtemUdpConnection>.Instance, port: 0));
    }

    [Fact]
    public void ImplementsIAtemConnection()
    {
        var conn = new AtemUdpConnection("127.0.0.1", NullLogger<AtemUdpConnection>.Instance);
        Assert.IsAssignableFrom<IAtemConnection>(conn);
    }

    [Fact]
    public void ImplementsIDeviceConnection()
    {
        var conn = new AtemUdpConnection("127.0.0.1", NullLogger<AtemUdpConnection>.Instance);
        Assert.IsAssignableFrom<ProdControlAV.Agent.Interfaces.IDeviceConnection>(conn);
    }

    [Fact]
    public void CurrentState_BeforeConnect_IsNull()
    {
        var conn = new AtemUdpConnection("127.0.0.1", NullLogger<AtemUdpConnection>.Instance);
        Assert.Null(conn.CurrentState);
    }
}

// ── AtemUdpConnection handshake and lifecycle tests ───────────────────────────

public class AtemUdpConnectionLifecycleTests
{
    /// <summary>
    /// Simulates the server side of an ATEM handshake:
    /// 1. Wait for client Hello
    /// 2. Send Init response (with assigned session ID)
    /// 3. Send ACK for client's first command packet
    /// </summary>
    private static Task RunAtemServerAsync(
        UdpClient server,
        ushort assignedSessionId,
        CancellationToken ct,
        Func<UdpClient, IPEndPoint, ushort, Task>? afterHandshake = null)
    {
        return Task.Run(async () =>
        {
            // Receive the Hello packet.
            var result = await server.ReceiveAsync(ct);
            var clientEp = result.RemoteEndPoint;

            // Validate it looks like a Hello (byte[0] & 0x10 != 0).
            Assert.True((result.Buffer[0] & AtemTestHelper.FlagHello) != 0,
                "Expected a Hello packet from the client.");

            // Send Init response.
            var initPkt = AtemTestHelper.BuildInitResponse(assignedSessionId, serverPacketId: 1);
            await server.SendAsync(initPkt, initPkt.Length, clientEp);

            if (afterHandshake != null)
                await afterHandshake(server, clientEp, assignedSessionId);
        }, ct);
    }

    [Fact]
    public async Task ConnectAsync_CompletesHandshake_IsConnected()
    {
        var (server, port) = AtemTestHelper.StartServer();
        await using var conn = new AtemUdpConnection("127.0.0.1", NullLogger<AtemUdpConnection>.Instance, port);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serverTask = RunAtemServerAsync(server, 0xABCD, cts.Token);

        await conn.ConnectAsync(cts.Token);

        Assert.True(conn.IsConnected);
        Assert.Equal(AtemConnectionState.Connected, conn.ConnectionState);

        server.Dispose();
        await serverTask;
    }

    [Fact]
    public async Task DisconnectAsync_AfterConnect_IsDisconnected()
    {
        var (server, port) = AtemTestHelper.StartServer();
        await using var conn = new AtemUdpConnection("127.0.0.1", NullLogger<AtemUdpConnection>.Instance, port);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serverTask = RunAtemServerAsync(server, 0xABCD, cts.Token);

        await conn.ConnectAsync(cts.Token);
        await conn.DisconnectAsync();

        Assert.False(conn.IsConnected);
        Assert.Equal(AtemConnectionState.Disconnected, conn.ConnectionState);

        server.Dispose();
        try { await serverTask; } catch (OperationCanceledException) { /* server cancelled */ }
    }

    [Fact]
    public async Task ConnectAsync_NoServer_ThrowsTimeoutException()
    {
        var (server, port) = AtemTestHelper.StartServer();
        server.Dispose(); // Close the server immediately so nobody responds.

        await using var conn = new AtemUdpConnection("127.0.0.1", NullLogger<AtemUdpConnection>.Instance, port);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        await Assert.ThrowsAsync<TimeoutException>(() => conn.ConnectAsync(cts.Token));
    }

    [Fact]
    public async Task ConnectionStateChanged_RaisedOnConnect()
    {
        var (server, port) = AtemTestHelper.StartServer();
        await using var conn = new AtemUdpConnection("127.0.0.1", NullLogger<AtemUdpConnection>.Instance, port);

        var states = new System.Collections.Generic.List<AtemConnectionState>();
        conn.ConnectionStateChanged += (_, s) => states.Add(s);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serverTask = RunAtemServerAsync(server, 0xABCD, cts.Token);

        await conn.ConnectAsync(cts.Token);

        Assert.Contains(AtemConnectionState.Connecting, states);
        Assert.Contains(AtemConnectionState.Connected,  states);

        server.Dispose();
        await serverTask;
    }

    [Fact]
    public async Task StateChanged_RaisedOnInboundStateUpdate()
    {
        var (server, port) = AtemTestHelper.StartServer();
        await using var conn = new AtemUdpConnection("127.0.0.1", NullLogger<AtemUdpConnection>.Instance, port);

        AtemState? received = null;
        conn.StateChanged += (_, s) => received = s;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var serverTask = RunAtemServerAsync(server, 0xABCD, cts.Token,
            afterHandshake: async (svr, clientEp, sid) =>
            {
                // Send a state-update packet with PrgI (program input = 2).
                var statePkt = AtemTestHelper.BuildStatePacket(
                    sid, packetId: 2,
                    cmdName: "PrgI",
                    cmdData: new byte[] { 0, 0, 0, 2 });
                await svr.SendAsync(statePkt, statePkt.Length, clientEp);

                // Wait for the client to ACK it.
                await Task.Delay(300, cts.Token);
            });

        await conn.ConnectAsync(cts.Token);

        // Give the receive loop a moment to process the state packet.
        await Task.Delay(500, cts.Token);

        Assert.NotNull(received);
        Assert.Equal(2, received!.ProgramInputId);

        // CurrentState should also reflect the update.
        Assert.NotNull(conn.CurrentState);
        Assert.Equal(2, conn.CurrentState!.ProgramInputId);

        server.Dispose();
        await serverTask;
    }
}

// ── AtemUdpConnection command sending tests ───────────────────────────────────

public class AtemUdpConnectionCommandTests
{
    /// <summary>
    /// Runs the ATEM handshake on the server side and returns the client endpoint.
    /// </summary>
    private static async Task<IPEndPoint> CompleteHandshakeAsync(
        UdpClient server, ushort sessionId, CancellationToken ct)
    {
        var result   = await server.ReceiveAsync(ct);
        var clientEp = result.RemoteEndPoint;

        Assert.True((result.Buffer[0] & AtemTestHelper.FlagHello) != 0,
            "Expected a Hello packet from the client.");

        var init = AtemTestHelper.BuildInitResponse(sessionId, serverPacketId: 1);
        await server.SendAsync(init, init.Length, clientEp);
        return clientEp;
    }

    /// <summary>
    /// Drains UDP datagrams from the server until one matching <paramref name="predicate"/>
    /// is found, then returns it.  Returns null if the deadline is exceeded.
    /// </summary>
    private static async Task<UdpReceiveResult?> DrainUntilAsync(
        UdpClient server,
        Func<byte[], bool> predicate,
        CancellationToken ct,
        TimeSpan? timeout = null)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout ?? TimeSpan.FromSeconds(3));

        while (!timeoutCts.IsCancellationRequested)
        {
            try
            {
                var r = await server.ReceiveAsync(timeoutCts.Token);
                if (predicate(r.Buffer))
                    return r;
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
        return null;
    }

    [Fact]
    public async Task CutToProgramAsync_SendsCommandAndAcks()
    {
        var (server, port) = AtemTestHelper.StartServer();
        await using var conn = new AtemUdpConnection("127.0.0.1", NullLogger<AtemUdpConnection>.Instance, port);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        const ushort sid = 0x1234;

        // --- Handshake phase ---
        var handshakeServer = Task.Run(() => CompleteHandshakeAsync(server, sid, cts.Token), cts.Token);
        await conn.ConnectAsync(cts.Token);
        var clientEp = await handshakeServer;

        // --- Command phase: client sends, server ACKs ---
        var cmdTask = conn.CutToProgramAsync(1, cts.Token);

        // Drain until we see the CPgI command packet.
        var cmdPacket = await DrainUntilAsync(server, buf =>
            (buf[0] & AtemTestHelper.FlagAckRequest) != 0 &&
            buf.Length > AtemTestHelper.HeaderSize &&
            Encoding.ASCII.GetString(buf, AtemTestHelper.HeaderSize + 4, 4) == "CPgI", cts.Token);

        Assert.NotNull(cmdPacket);
        ushort clientSeq = (ushort)((cmdPacket!.Value.Buffer[10] << 8) | cmdPacket.Value.Buffer[11]);
        var ack = AtemTestHelper.BuildAck(sid, ackId: clientSeq);
        await server.SendAsync(ack, ack.Length, clientEp);

        await cmdTask.WaitAsync(TimeSpan.FromSeconds(3));

        server.Dispose();
    }

    [Fact]
    public async Task SetPreviewAsync_SendsCPvI()
    {
        var (server, port) = AtemTestHelper.StartServer();
        await using var conn = new AtemUdpConnection("127.0.0.1", NullLogger<AtemUdpConnection>.Instance, port);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        const ushort sid = 0x2345;

        var handshakeServer = Task.Run(() => CompleteHandshakeAsync(server, sid, cts.Token), cts.Token);
        await conn.ConnectAsync(cts.Token);
        var clientEp = await handshakeServer;

        var cmdTask = conn.SetPreviewAsync(3, cts.Token);

        var cmdPacket = await DrainUntilAsync(server, buf =>
            (buf[0] & AtemTestHelper.FlagAckRequest) != 0 &&
            buf.Length > AtemTestHelper.HeaderSize &&
            Encoding.ASCII.GetString(buf, AtemTestHelper.HeaderSize + 4, 4) == "CPvI", cts.Token);

        Assert.NotNull(cmdPacket);
        ushort clientSeq = (ushort)((cmdPacket!.Value.Buffer[10] << 8) | cmdPacket.Value.Buffer[11]);
        var ack = AtemTestHelper.BuildAck(sid, ackId: clientSeq);
        await server.SendAsync(ack, ack.Length, clientEp);

        await cmdTask.WaitAsync(TimeSpan.FromSeconds(3));

        server.Dispose();
    }

    [Fact]
    public async Task CutToProgramAsync_WhenNotConnected_Throws()
    {
        var (server, port) = AtemTestHelper.StartServer();
        await using var conn = new AtemUdpConnection("127.0.0.1", NullLogger<AtemUdpConnection>.Instance, port);

        await Assert.ThrowsAsync<InvalidOperationException>(() => conn.CutToProgramAsync(1));

        server.Dispose();
    }

    [Fact]
    public async Task CutToProgramAsync_InvalidInput_Throws()
    {
        var (server, port) = AtemTestHelper.StartServer();
        await using var conn = new AtemUdpConnection("127.0.0.1", NullLogger<AtemUdpConnection>.Instance, port);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => conn.CutToProgramAsync(0));

        server.Dispose();
    }
}
