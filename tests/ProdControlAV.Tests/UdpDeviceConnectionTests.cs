using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ProdControlAV.Agent.Models;
using ProdControlAV.Agent.Services;
using Xunit;

namespace ProdControlAV.Tests;

// ── Concrete stub used by all tests ──────────────────────────────────────────

/// <summary>
/// Minimal concrete subclass of <see cref="BaseUdpDeviceConnection"/> for unit tests.
/// Commands are encoded as UTF-8 strings; responses are decoded the same way.
/// A response is considered successful when the payload starts with "OK".
/// </summary>
file sealed class StubUdpConnection : BaseUdpDeviceConnection
{
    protected override string DeviceTypeName => "StubUdp";

    public StubUdpConnection(string host, int port, ILogger logger)
        : base(host, port, logger) { }

    protected override byte[] BuildDatagramFromCommand(string command, UdpProtocolContext ctx)
        => Encoding.UTF8.GetBytes(command);

    protected override bool TryParseDeviceResponse(ReceivedDatagram rx, out DeviceResponse response)
    {
        var text = Encoding.UTF8.GetString(rx.Data);
        response = new DeviceResponse
        {
            Success = text.StartsWith("OK", StringComparison.OrdinalIgnoreCase),
            StatusCode = text.StartsWith("OK", StringComparison.OrdinalIgnoreCase) ? 200 : 500,
            Message = text
        };
        return true;
    }
}

/// <summary>
/// Concrete stub that requires a handshake.
/// The handshake packet is "HELLO"; the server must echo back "HELLOACK".
/// </summary>
file sealed class HandshakeUdpConnection : BaseUdpDeviceConnection
{
    protected override string DeviceTypeName => "HandshakeUdp";
    protected override bool RequiresHandshake => true;

    public HandshakeUdpConnection(string host, int port, ILogger logger)
        : base(host, port, logger) { }

    protected override async Task SendHandshakeAsync(CancellationToken ct)
    {
        // The protected SendCommandAsync path is not available here – call the socket directly.
        // We test the base handshake machinery by overriding at a lower level.
        // Nothing to send in this stub; the test server drives the ACK.
        await Task.CompletedTask;
    }

    protected override bool IsHandshakeResponse(ReceivedDatagram rx)
        => Encoding.UTF8.GetString(rx.Data) == "HELLOACK";

    protected override byte[] BuildDatagramFromCommand(string command, UdpProtocolContext ctx)
        => Encoding.UTF8.GetBytes(command);

    protected override bool TryParseDeviceResponse(ReceivedDatagram rx, out DeviceResponse response)
    {
        var text = Encoding.UTF8.GetString(rx.Data);
        response = new DeviceResponse
        {
            Success = true,
            StatusCode = 200,
            Message = text
        };
        return true;
    }
}

// ── Test helpers ──────────────────────────────────────────────────────────────

file static class UdpTestServer
{
    /// <summary>Starts a local UDP server on a random port and returns the port + server socket.</summary>
    public static (UdpClient server, int port) Start()
    {
        var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;
        return (server, port);
    }
}

// ── Constructor validation ────────────────────────────────────────────────────

public class BaseUdpDeviceConnectionConstructorTests
{
    [Fact]
    public void NullHost_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new StubUdpConnection(null!, 9910, NullLogger.Instance));
    }

    [Fact]
    public void EmptyHost_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new StubUdpConnection("", 9910, NullLogger.Instance));
    }

    [Fact]
    public void PortZero_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new StubUdpConnection("127.0.0.1", 0, NullLogger.Instance));
    }

    [Fact]
    public void PortOverMax_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new StubUdpConnection("127.0.0.1", 65536, NullLogger.Instance));
    }

    [Fact]
    public void ValidArgs_IsConnectedFalseInitially()
    {
        var conn = new StubUdpConnection("127.0.0.1", 9910, NullLogger.Instance);
        Assert.False(conn.IsConnected);
    }
}

// ── IDeviceConnection contract ────────────────────────────────────────────────

public class BaseUdpDeviceConnectionContractTests
{
    [Fact]
    public void StubUdpConnection_ImplementsIDeviceConnection()
    {
        var conn = new StubUdpConnection("127.0.0.1", 9910, NullLogger.Instance);
        Assert.IsAssignableFrom<ProdControlAV.Agent.Interfaces.IDeviceConnection>(conn);
    }
}

// ── Session lifecycle ─────────────────────────────────────────────────────────

public class BaseUdpDeviceConnectionLifecycleTests
{
    [Fact]
    public async Task StartAsync_WithoutHandshake_SetsIsConnectedTrue()
    {
        var (server, port) = UdpTestServer.Start();
        await using var conn = new StubUdpConnection("127.0.0.1", port, NullLogger.Instance);

        await conn.StartAsync();

        Assert.True(conn.IsConnected);

        server.Dispose();
    }

    [Fact]
    public async Task DisconnectAsync_SetsIsConnectedFalse()
    {
        var (server, port) = UdpTestServer.Start();
        await using var conn = new StubUdpConnection("127.0.0.1", port, NullLogger.Instance);

        await conn.StartAsync();
        Assert.True(conn.IsConnected);

        await conn.DisconnectAsync();
        Assert.False(conn.IsConnected);

        server.Dispose();
    }

    [Fact]
    public async Task DisposeAsync_DoesNotThrow()
    {
        var (server, port) = UdpTestServer.Start();
        var conn = new StubUdpConnection("127.0.0.1", port, NullLogger.Instance);
        await conn.StartAsync();

        // Should not throw
        await conn.DisposeAsync();
        server.Dispose();
    }
}

// ── Send / Receive ────────────────────────────────────────────────────────────

public class BaseUdpDeviceConnectionSendReceiveTests
{
    [Fact]
    public async Task SendCommandAsync_ServerResponds_ReturnsDeviceResponse()
    {
        var (server, port) = UdpTestServer.Start();
        await using var conn = new StubUdpConnection("127.0.0.1", port, NullLogger.Instance);
        await conn.StartAsync();

        // Background server: echo "OK hello" back to sender.
        var serverTask = Task.Run(async () =>
        {
            var result = await server.ReceiveAsync();
            var reply = Encoding.UTF8.GetBytes("OK hello");
            await server.SendAsync(reply, reply.Length, result.RemoteEndPoint);
        });

        var response = await conn.SendCommandAsync("ping", TimeSpan.FromSeconds(5));

        Assert.True(response.Success);
        Assert.Equal(200, response.StatusCode);
        Assert.Equal("OK hello", response.Message);

        server.Dispose();
        await serverTask;
    }

    [Fact]
    public async Task SendCommandAsync_WithExplicitTimeout_ReturnsDeviceResponse()
    {
        var (server, port) = UdpTestServer.Start();
        await using var conn = new StubUdpConnection("127.0.0.1", port, NullLogger.Instance);
        await conn.StartAsync();

        var serverTask = Task.Run(async () =>
        {
            var result = await server.ReceiveAsync();
            var reply = Encoding.UTF8.GetBytes("OK explicit-timeout-test");
            await server.SendAsync(reply, reply.Length, result.RemoteEndPoint);
        });

        var response = await conn.SendCommandAsync("cmd", TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.IsType<DeviceResponse>(response);
        Assert.Equal(200, response.StatusCode);

        server.Dispose();
        await serverTask;
    }

    [Fact]
    public async Task SendCommandAsync_NoResponse_ThrowsTimeoutException()
    {
        var (server, port) = UdpTestServer.Start();
        await using var conn = new StubUdpConnection("127.0.0.1", port, NullLogger.Instance);
        await conn.StartAsync();

        // Do NOT respond from the server.
        await Assert.ThrowsAsync<TimeoutException>(() =>
            conn.SendCommandAsync("ping", TimeSpan.FromMilliseconds(300)));

        server.Dispose();
    }
}

// ── Timeout / cancellation ────────────────────────────────────────────────────

public class BaseUdpDeviceConnectionTimeoutTests
{
    [Fact]
    public async Task SendCommandAsync_Timeout_DoesNotHangForever()
    {
        var (server, port) = UdpTestServer.Start();
        await using var conn = new StubUdpConnection("127.0.0.1", port, NullLogger.Instance);
        await conn.StartAsync();

        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            await conn.SendCommandAsync("test", TimeSpan.FromMilliseconds(200));
        }
        catch (TimeoutException) { /* expected */ }

        sw.Stop();

        // Allow a generous margin for CI.
        Assert.True(sw.ElapsedMilliseconds < 3000, $"Command took too long: {sw.ElapsedMilliseconds}ms");

        server.Dispose();
    }
}

// ── Disconnect fails pending command ─────────────────────────────────────────

public class BaseUdpDeviceConnectionDisconnectTests
{
    /// <summary>Generous timeout used when waiting for a task to resolve in CI environments.</summary>
    private static readonly TimeSpan CiTaskTimeout = TimeSpan.FromSeconds(3);

    [Fact]
    public async Task DisconnectAsync_FailsPendingCommandWithException()
    {
        var (server, port) = UdpTestServer.Start();
        await using var conn = new StubUdpConnection("127.0.0.1", port, NullLogger.Instance);
        await conn.StartAsync();

        // Start a command that will never get a response, then disconnect.
        var commandTask = conn.SendCommandAsync("hang", TimeSpan.FromSeconds(10));

        // Give the send loop time to enqueue the command.
        await Task.Delay(100);
        await conn.DisconnectAsync();

        // The pending command must resolve (via timeout or exception), not hang forever.
        var completed = await Task.WhenAny(commandTask, Task.Delay(CiTaskTimeout));
        Assert.Equal(commandTask, completed);

        server.Dispose();
    }
}

// ── Malformed datagram robustness ─────────────────────────────────────────────

public class BaseUdpDeviceConnectionRobustnessTests
{
    /// <summary>
    /// A stub that throws on every parse call to simulate malformed datagrams.
    /// </summary>
    private sealed class ThrowingUdpConnection : BaseUdpDeviceConnection
    {
        protected override string DeviceTypeName => "ThrowingUdp";

        public int ParseCallCount { get; private set; }

        public ThrowingUdpConnection(string host, int port, ILogger logger)
            : base(host, port, logger) { }

        protected override byte[] BuildDatagramFromCommand(string command, UdpProtocolContext ctx)
            => Encoding.UTF8.GetBytes(command);

        protected override bool TryParseDeviceResponse(ReceivedDatagram rx, out DeviceResponse response)
        {
            ParseCallCount++;
            response = default!;
            throw new InvalidOperationException("Simulated parse failure");
        }
    }

    [Fact]
    public async Task ReceiveLoop_MalformedDatagram_DoesNotCrashLoop()
    {
        var (server, port) = UdpTestServer.Start();
        await using var conn = new ThrowingUdpConnection("127.0.0.1", port, NullLogger.Instance);
        await conn.StartAsync();

        // Send a command so the stub can receive a datagram.
        var commandTask = conn.SendCommandAsync("cmd", TimeSpan.FromMilliseconds(500));

        // Send a response that will trigger the throwing parser.
        await Task.Delay(50);
        var clientEp = new IPEndPoint(IPAddress.Loopback, port);
        var junk = Encoding.UTF8.GetBytes("JUNK");

        // The server needs to know the client's port to reply.
        var received = await server.ReceiveAsync();
        await server.SendAsync(junk, junk.Length, received.RemoteEndPoint);

        // The receive loop should survive the parse error; the command will timeout.
        await Assert.ThrowsAsync<TimeoutException>(() => commandTask);

        // Verify the loop is still alive by checking IsConnected hasn't been torn down by an unhandled exception.
        Assert.True(conn.IsConnected);

        server.Dispose();
    }
}

// ── ReceivedDatagram model ────────────────────────────────────────────────────

public class ReceivedDatagramTests
{
    [Fact]
    public void DefaultReceivedDatagram_HasEmptyData()
    {
        var rx = new ReceivedDatagram();
        Assert.Empty(rx.Data);
    }

    [Fact]
    public void ReceivedDatagram_StoresDataAndEndpoint()
    {
        var ep = new IPEndPoint(IPAddress.Loopback, 1234);
        var rx = new ReceivedDatagram { Data = new byte[] { 1, 2, 3 }, RemoteEndPoint = ep };

        Assert.Equal(3, rx.Data.Length);
        Assert.Equal(ep, rx.RemoteEndPoint);
    }
}

// ── UdpProtocolContext ────────────────────────────────────────────────────────

public class UdpProtocolContextTests
{
    [Fact]
    public void DefaultContext_HasZeroSequences()
    {
        var ctx = new UdpProtocolContext();
        Assert.Equal(0, ctx.OutboundSequence);
        Assert.Equal(0, ctx.LastReceivedSequence);
        Assert.Equal(0, ctx.SessionId);
    }
}
