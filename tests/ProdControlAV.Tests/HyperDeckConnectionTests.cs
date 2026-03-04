using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using ProdControlAV.Agent.Services;
using Xunit;

namespace ProdControlAV.Tests;

public class HyperDeckConnectionTests
{
    // ── ParseBlock ────────────────────────────────────────────────────────────

    [Fact]
    public void ParseBlock_EmptyLines_ReturnsDefaultResponse()
    {
        var response = HyperDeckConnection.ParseBlock(Array.Empty<string>());

        Assert.Equal(0, response.StatusCode);
        Assert.Equal(string.Empty, response.StatusText);
        Assert.Empty(response.Fields);
    }

    [Fact]
    public void ParseBlock_StatusOnlyLine_ParsesCodeAndText()
    {
        var lines = new[] { "200 ok" };
        var response = HyperDeckConnection.ParseBlock(lines);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("ok", response.StatusText);
        Assert.Empty(response.Fields);
    }

    [Fact]
    public void ParseBlock_WithFields_ParsesAllKeyValuePairs()
    {
        var lines = new[]
        {
            "200 ok",
            "transport: play",
            "speed: 100",
            "slot id: 1"
        };

        var response = HyperDeckConnection.ParseBlock(lines);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("ok", response.StatusText);
        Assert.Equal("play", response.Fields["transport"]);
        Assert.Equal("100", response.Fields["speed"]);
        Assert.Equal("1", response.Fields["slot id"]);
    }

    [Fact]
    public void ParseBlock_ErrorResponse_ParsesErrorCode()
    {
        var lines = new[] { "500 error", "description: invalid command" };
        var response = HyperDeckConnection.ParseBlock(lines);

        Assert.Equal(500, response.StatusCode);
        Assert.Equal("error", response.StatusText);
        Assert.Equal("invalid command", response.Fields["description"]);
    }

    [Fact]
    public void ParseBlock_FieldKeysAreCaseInsensitive()
    {
        var lines = new[] { "200 ok", "Transport: play" };
        var response = HyperDeckConnection.ParseBlock(lines);

        // Dictionary should be case-insensitive
        Assert.Equal("play", response.Fields["transport"]);
        Assert.Equal("play", response.Fields["Transport"]);
        Assert.Equal("play", response.Fields["TRANSPORT"]);
    }

    [Fact]
    public void ParseBlock_LineWithoutColon_IsIgnored()
    {
        var lines = new[] { "200 ok", "no colon here" };
        var response = HyperDeckConnection.ParseBlock(lines);

        Assert.Equal(200, response.StatusCode);
        Assert.Empty(response.Fields);
    }

    [Fact]
    public void ParseBlock_NoSpaceOnFirstLine_StatusCodeIsZero()
    {
        var lines = new[] { "unexpected" };
        var response = HyperDeckConnection.ParseBlock(lines);

        Assert.Equal(0, response.StatusCode);
        Assert.Equal("unexpected", response.StatusText);
    }

    [Fact]
    public void ParseBlock_MultipleColonsInValue_PreservesFullValue()
    {
        // Values may contain colons (e.g. timecodes "00:01:02:03")
        var lines = new[] { "200 ok", "timecode: 00:01:02:03" };
        var response = HyperDeckConnection.ParseBlock(lines);

        Assert.Equal("00:01:02:03", response.Fields["timecode"]);
    }

    // ── Constructor validation ────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullHost_ThrowsArgumentException()
    {
        var logger = Mock.Of<ILogger<HyperDeckConnectionPool>>();
        Assert.Throws<ArgumentException>(() =>
            new HyperDeckConnection(null!, 9993, logger));
    }

    [Fact]
    public void Constructor_EmptyHost_ThrowsArgumentException()
    {
        var logger = Mock.Of<ILogger<HyperDeckConnectionPool>>();
        Assert.Throws<ArgumentException>(() =>
            new HyperDeckConnection("", 9993, logger));
    }

    [Fact]
    public void Constructor_InvalidPort_ThrowsArgumentOutOfRangeException()
    {
        var logger = Mock.Of<ILogger<HyperDeckConnectionPool>>();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new HyperDeckConnection("192.168.1.1", 0, logger));
    }

    [Fact]
    public void Constructor_ValidArguments_InitialisesInDisconnectedState()
    {
        var logger = Mock.Of<ILogger<HyperDeckConnectionPool>>();
        var conn = new HyperDeckConnection("192.168.1.1", 9993, logger);
        Assert.Equal(HyperDeckConnectionState.Disconnected, conn.ConnectionState);
    }

    // ── SendCommandAsync timeout ──────────────────────────────────────────────

    [Fact]
    public async Task SendCommandAsync_WhenNoTcpServer_ThrowsOnTimeout()
    {
        // Use a loopback address with an unlistened port so ConnectAsync itself
        // will fail, causing StartAsync to fail. We verify the exception propagates.
        var logger = Mock.Of<ILogger<HyperDeckConnectionPool>>();
        var conn = new HyperDeckConnection("127.0.0.1", 19993, logger);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        // StartAsync should throw because no server is listening
        await Assert.ThrowsAnyAsync<Exception>(() =>
            conn.StartAsync(cts.Token));

        await conn.DisposeAsync();
    }

    // ── Round-trip with a local TCP server ───────────────────────────────────

    [Fact]
    public async Task SendCommandAsync_WithLocalServer_ReturnsExpectedResponse()
    {
        // Arrange: start a minimal local TCP server that speaks HyperDeck protocol
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        // Server task: accept one connection, send a greeting, then reply to each command
        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var ns = client.GetStream();
            using var writer = new System.IO.StreamWriter(ns, Encoding.ASCII) { AutoFlush = true };
            using var reader = new System.IO.StreamReader(ns, Encoding.ASCII);

            // Send initial greeting (unsolicited)
            await writer.WriteAsync("500 connection info\r\nprotocol version: 1.11\r\n\r\n");

            // Read one command and send a play response
            var line = await reader.ReadLineAsync();
            if (line?.Trim() == "play")
                await writer.WriteAsync("200 ok\r\n\r\n");
        });

        // Act: connect and send command
        var logger = Mock.Of<ILogger<HyperDeckConnectionPool>>();
        await using var conn = new HyperDeckConnection("127.0.0.1", port, logger);
        await conn.StartAsync();

        // Give the greeting time to arrive and be consumed
        await Task.Delay(100);

        var response = await conn.SendCommandAsync("play");

        // Assert
        Assert.Equal(200, response.StatusCode);
        Assert.Equal("ok", response.Message);

        listener.Stop();
        await serverTask;
    }

    [Fact]
    public async Task SendCommandAsync_WithFieldsResponse_ParsesFieldsCorrectly()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var ns = client.GetStream();
            using var writer = new System.IO.StreamWriter(ns, Encoding.ASCII) { AutoFlush = true };
            using var reader = new System.IO.StreamReader(ns, Encoding.ASCII);

            // No greeting – go straight to command handling
            var line = await reader.ReadLineAsync();
            if (line?.Trim() == "transport info")
            {
                await writer.WriteAsync(
                    "208 transport info\r\n" +
                    "status: play\r\n" +
                    "speed: 100\r\n" +
                    "slot id: 1\r\n" +
                    "\r\n");
            }
        });

        var logger = Mock.Of<ILogger<HyperDeckConnectionPool>>();
        await using var conn = new HyperDeckConnection("127.0.0.1", port, logger);
        await conn.StartAsync();

        var response = await conn.SendCommandAsync("transport info");

        Assert.Equal(208, response.StatusCode);
        Assert.Equal("transport info", response.Message);
        Assert.Equal("play", response.Data["status"]);
        Assert.Equal("100", response.Data["speed"]);
        Assert.Equal("1", response.Data["slot id"]);

        listener.Stop();
        await serverTask;
    }
}

public class HyperDeckConnectionPoolTests
{
    [Fact]
    public async Task DisposeAsync_DoesNotThrow()
    {
        var logger = Mock.Of<ILogger<HyperDeckConnectionPool>>();
        var pool = new HyperDeckConnectionPool(logger);
        await pool.DisposeAsync(); // should not throw
    }

    [Fact]
    public async Task GetOrCreateAsync_ReturnsSameInstanceForSameKey()
    {
        // Use a listening server so connections succeed
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        // Accept connections silently
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    var client = await listener.AcceptTcpClientAsync();
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(5000); // keep alive
                        client.Dispose();
                    });
                }
                catch { break; }
            }
        });

        var mockLogger = new Mock<ILogger<HyperDeckConnectionPool>>();
        await using var pool = new HyperDeckConnectionPool(mockLogger.Object);

        var conn1 = await pool.GetOrCreateAsync("127.0.0.1", port);
        var conn2 = await pool.GetOrCreateAsync("127.0.0.1", port);

        Assert.Same(conn1, conn2);

        listener.Stop();
    }
}
