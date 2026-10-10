// <copyright file="BinaryProtocolDisconnectTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using SharpCoreDB.Server.Core;

namespace SharpCoreDB.Server.IntegrationTests;

/// <summary>
/// Regression tests for the way the binary protocol listener reports an ordinary client disconnect.
/// A peer that aborts its socket (TCP reset / error 10053) used to be logged as
/// "Authentication failed for user …" plus an IOException/SocketException stack at error level, and a
/// disconnect during the message loop as "Binary protocol error for client …" — neither is a server
/// fault, so both must now be an information line and must not count as a failed request.
/// </summary>
public sealed class BinaryProtocolDisconnectTests : IAsyncLifetime
{
    private const int ProtocolVersion3 = 196608;

    private readonly TestServerFixture _fixture = new();

    public async ValueTask InitializeAsync()
        => await _fixture.InitializeAsync();

    public async ValueTask DisposeAsync()
        => await _fixture.DisposeAsync();

    [Fact]
    public async Task HandleConnectionAsync_WhenClientAbortsDuringStartup_ReportsDisconnectInsteadOfAuthenticationFailure()
    {
        // Arrange
        var logger = new CapturingLogger();
        var metrics = _fixture.GetMetricsCollector();
        var failedRequestsBefore = metrics.GetSnapshot().FailedRequests;

        // Act — send a valid startup message, then abort the socket before the server can answer.
        await RunHandlerSessionAsync(logger, async (endpoint, cancellationToken) =>
        {
            using var socket = CreateAbortingSocket();
            await socket.ConnectAsync(endpoint, cancellationToken);
            await socket.SendAsync(CreateStartupPacket("admin", "testdb"), cancellationToken);
            socket.Close(); // SO_LINGER 0 → RST while the server writes the startup response
        });

        // Assert
        AssertDisconnectWasNotReportedAsFailure(logger);
        Assert.Equal(failedRequestsBefore, metrics.GetSnapshot().FailedRequests);
    }

    [Fact]
    public async Task HandleConnectionAsync_WhenClientAbortsMidMessage_ReportsDisconnectInsteadOfProtocolError()
    {
        // Arrange
        var logger = new CapturingLogger();
        var metrics = _fixture.GetMetricsCollector();
        var failedRequestsBefore = metrics.GetSnapshot().FailedRequests;

        // Act — complete the handshake, then close the socket in the middle of a message.
        await RunHandlerSessionAsync(logger, async (endpoint, cancellationToken) =>
        {
            using var socket = CreateAbortingSocket();
            await socket.ConnectAsync(endpoint, cancellationToken);
            await socket.SendAsync(CreateStartupPacket("admin", "testdb"), cancellationToken);
            await ReadUntilReadyForQueryAsync(socket, cancellationToken);

            // Announce a 1000-byte query message but send only its header.
            var header = new byte[5];
            header[0] = (byte)'Q';
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(1, 4), 1000);
            await socket.SendAsync(header, cancellationToken);

            socket.Close(); // RST with the message half-sent
        });

        // Assert
        AssertDisconnectWasNotReportedAsFailure(logger);
        Assert.Equal(failedRequestsBefore, metrics.GetSnapshot().FailedRequests);
    }

    private static void AssertDisconnectWasNotReportedAsFailure(CapturingLogger logger)
    {
        Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Error);
        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Message.Contains("Authentication failed", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Exception is SocketException or IOException);
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Information
                && entry.Message.Contains("disconnected", StringComparison.OrdinalIgnoreCase));
    }

    private async Task RunHandlerSessionAsync(
        CapturingLogger logger,
        Func<IPEndPoint, CancellationToken, Task> clientConversationAsync)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(clientConversationAsync);

        var cancellationToken = TestContext.Current.CancellationToken;
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            var acceptTask = listener.AcceptTcpClientAsync(cancellationToken).AsTask();
            var clientTask = clientConversationAsync(endpoint, cancellationToken);
            var serverClient = await acceptTask;

            await using var handler = _fixture.CreateBinaryProtocolHandler(logger);
            var serverTask = handler.HandleConnectionAsync(serverClient, cancellationToken);

            await clientTask;

            var finished = await Task.WhenAny(serverTask, Task.Delay(TimeSpan.FromSeconds(15), cancellationToken));
            Assert.True(
                ReferenceEquals(finished, serverTask),
                "The binary protocol handler did not finish after the client aborted its connection.");
            await serverTask;
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>A socket that aborts with a TCP reset (SO_LINGER 0) instead of a graceful FIN.</summary>
    private static Socket CreateAbortingSocket()
        => new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            LingerState = new LingerOption(enable: true, seconds: 0),
        };

    private static async Task ReadUntilReadyForQueryAsync(Socket socket, CancellationToken cancellationToken)
    {
        var header = new byte[5];
        while (true)
        {
            await ReadExactlyAsync(socket, header, cancellationToken);

            var length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(1, 4)) - 4;
            var payload = new byte[length];
            if (length > 0)
            {
                await ReadExactlyAsync(socket, payload, cancellationToken);
            }

            if (header[0] == (byte)'Z')
            {
                return;
            }
        }
    }

    private static async Task ReadExactlyAsync(Socket socket, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await socket.ReceiveAsync(
                buffer.AsMemory(offset, buffer.Length - offset), SocketFlags.None, cancellationToken);
            Assert.NotEqual(0, read);
            offset += read;
        }
    }

    private static byte[] CreateStartupPacket(string user, string database)
    {
        var payload = new List<byte>();
        AppendInt32(payload, ProtocolVersion3);
        AppendCString(payload, "user");
        AppendCString(payload, user);
        AppendCString(payload, "database");
        AppendCString(payload, database);
        AppendCString(payload, "application_name");
        AppendCString(payload, "SharpCoreDB.Tests");
        payload.Add(0);

        var packet = new byte[payload.Count + 4];
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(0, 4), packet.Length);
        payload.CopyTo(packet, 4);
        return packet;
    }

    private static void AppendInt32(List<byte> buffer, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        buffer.AddRange(bytes.ToArray());
    }

    private static void AppendCString(List<byte> buffer, string value)
    {
        buffer.AddRange(Encoding.UTF8.GetBytes(value));
        buffer.Add(0);
    }

    /// <summary>Collects everything a single <see cref="BinaryProtocolHandler"/> logs.</summary>
    private sealed class CapturingLogger : ILogger<BinaryProtocolHandler>
    {
        private readonly List<LogEntry> _entries = [];

        public IReadOnlyList<LogEntry> Entries => _entries;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            _entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    private readonly record struct LogEntry(LogLevel Level, string Message, Exception? Exception);
}
