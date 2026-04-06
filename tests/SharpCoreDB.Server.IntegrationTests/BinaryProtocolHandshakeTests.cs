// <copyright file="BinaryProtocolHandshakeTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SharpCoreDB.Server.IntegrationTests;

/// <summary>
/// End-to-end protocol handshake tests for the PostgreSQL-compatible binary listener.
/// Validates startup negotiation, parameter status reporting, and deterministic auth failures.
/// </summary>
public sealed class BinaryProtocolHandshakeTests : IAsyncLifetime
{
    private const int SslRequestCode = 80877103;
    private const int ProtocolVersion3 = 196608;

    private readonly TestServerFixture _fixture = new();

    public async ValueTask InitializeAsync()
        => await _fixture.InitializeAsync();

    public async ValueTask DisposeAsync()
        => await _fixture.DisposeAsync();

    [Fact]
    public async Task HandleConnectionAsync_WithSslRequestFollowedByStartup_ShouldContinueHandshake()
    {
        // Arrange
        var messages = await ExecuteBinaryConversationAsync(async (stream, cancellationToken) =>
        {
            await stream.WriteAsync(CreateSslRequestPacket(), cancellationToken);

            var sslResponse = new byte[1];
            await stream.ReadExactlyAsync(sslResponse, cancellationToken);
            Assert.Equal((byte)'N', sslResponse[0]);

            await stream.WriteAsync(CreateStartupPacket("admin", "testdb", "psql"), cancellationToken);
            return await ReadMessagesUntilAsync(stream, static message => message.Type == (byte)'Z', cancellationToken);
        });

        // Assert
        Assert.Equal((byte)'R', messages[0].Type);
        Assert.Contains(messages, static message => message.Type == (byte)'K');
        Assert.Equal((byte)'Z', messages[^1].Type);
    }

    [Fact]
    public async Task HandleConnectionAsync_WithApplicationName_ShouldEmitExpectedParameterStatuses()
    {
        // Arrange
        var messages = await ExecuteBinaryConversationAsync(async (stream, cancellationToken) =>
        {
            await stream.WriteAsync(CreateStartupPacket("admin", "testdb", "DBeaver"), cancellationToken);
            return await ReadMessagesUntilAsync(stream, static message => message.Type == (byte)'Z', cancellationToken);
        });

        var parameters = messages
            .Where(static message => message.Type == (byte)'S')
            .Select(ParseParameterStatus)
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        // Assert
        Assert.Equal("16.0", parameters["server_version"]);
        Assert.Equal("160000", parameters["server_version_num"]);
        Assert.Equal("DBeaver", parameters["application_name"]);
        Assert.Equal("admin", parameters["session_authorization"]);
        Assert.Equal("on", parameters["is_superuser"]);
        Assert.Equal("on", parameters["standard_conforming_strings"]);
        Assert.Equal("UTC", parameters["TimeZone"]);
    }

    [Fact]
    public async Task HandleConnectionAsync_WithUnknownUser_ShouldReturnFatalAuthenticationError()
    {
        // Arrange
        var error = await ExecuteBinaryConversationAsync(async (stream, cancellationToken) =>
        {
            await stream.WriteAsync(CreateStartupPacket("missing-user", "testdb"), cancellationToken);
            var message = await ReadBackendMessageAsync(stream, cancellationToken);
            return ParseErrorFields(message);
        });

        // Assert
        Assert.Equal("FATAL", error["S"]);
        Assert.Equal("28000", error["C"]);
    }

    [Fact]
    public async Task HandleConnectionAsync_WithUnknownDatabase_ShouldReturnInvalidCatalogError()
    {
        // Arrange
        var error = await ExecuteBinaryConversationAsync(async (stream, cancellationToken) =>
        {
            await stream.WriteAsync(CreateStartupPacket("admin", "missing-db"), cancellationToken);
            var message = await ReadBackendMessageAsync(stream, cancellationToken);
            return ParseErrorFields(message);
        });

        // Assert
        Assert.Equal("FATAL", error["S"]);
        Assert.Equal("3D000", error["C"]);
    }

    [Fact]
    public async Task HandleConnectionAsync_WithCreateTableQuery_ShouldReturnCreateTableCommandTag()
    {
        // Arrange
        var commandTag = await ExecuteBinaryConversationAsync(async (stream, cancellationToken) =>
        {
            await stream.WriteAsync(CreateStartupPacket("admin", "testdb", "psql"), cancellationToken);
            _ = await ReadMessagesUntilAsync(stream, static message => message.Type == (byte)'Z', cancellationToken);

            await stream.WriteAsync(CreateQueryPacket("CREATE TABLE IF NOT EXISTS phase4_command_tag_test (id INTEGER)"), cancellationToken);
            var queryMessages = await ReadMessagesUntilAsync(stream, static message => message.Type == (byte)'Z', cancellationToken);
            var commandMessage = Assert.Single(queryMessages.Where(static message => message.Type == (byte)'C'));
            return ParseCommandComplete(commandMessage);
        });

        // Assert
        Assert.Equal("CREATE TABLE", commandTag);
    }

    [Fact]
    public async Task HandleConnectionAsync_WithSelectQuery_ShouldReturnSelectCommandTagWithRowCount()
    {
        // Arrange
        var commandTag = await ExecuteBinaryConversationAsync(async (stream, cancellationToken) =>
        {
            await stream.WriteAsync(CreateStartupPacket("admin", "testdb", "psql"), cancellationToken);
            _ = await ReadMessagesUntilAsync(stream, static message => message.Type == (byte)'Z', cancellationToken);

            await stream.WriteAsync(CreateQueryPacket("SELECT 1 AS value"), cancellationToken);
            var queryMessages = await ReadMessagesUntilAsync(stream, static message => message.Type == (byte)'Z', cancellationToken);
            var commandMessage = Assert.Single(queryMessages.Where(static message => message.Type == (byte)'C'));
            return ParseCommandComplete(commandMessage);
        });

        // Assert
        Assert.Equal("SELECT 1", commandTag);
    }

    private async Task<TResult> ExecuteBinaryConversationAsync<TResult>(
        Func<NetworkStream, CancellationToken, Task<TResult>> clientConversationAsync)
    {
        ArgumentNullException.ThrowIfNull(clientConversationAsync);

        var cancellationToken = TestContext.Current.CancellationToken;
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(endpoint.Address, endpoint.Port, cancellationToken);
            var serverClient = await listener.AcceptTcpClientAsync(cancellationToken);
            await connectTask;

            await using var handler = _fixture.CreateBinaryProtocolHandler();
            var serverTask = handler.HandleConnectionAsync(serverClient, cancellationToken);
            await using var stream = client.GetStream();

            var result = await clientConversationAsync(stream, cancellationToken);
            client.Close();
            await serverTask;
            return result;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<List<BackendMessage>> ReadMessagesUntilAsync(
        NetworkStream stream,
        Func<BackendMessage, bool> stopPredicate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(stopPredicate);

        List<BackendMessage> messages = [];
        while (true)
        {
            var message = await ReadBackendMessageAsync(stream, cancellationToken);
            messages.Add(message);
            if (stopPredicate(message))
            {
                return messages;
            }
        }
    }

    private static async Task<BackendMessage> ReadBackendMessageAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var header = new byte[5];
        await stream.ReadExactlyAsync(header, cancellationToken);

        var length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(1, 4));
        var payload = new byte[length - 4];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        return new BackendMessage(header[0], payload);
    }

    private static KeyValuePair<string, string> ParseParameterStatus(BackendMessage message)
    {
        Assert.Equal((byte)'S', message.Type);

        var parts = Encoding.UTF8.GetString(message.Payload)
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);

        return new KeyValuePair<string, string>(parts[0], parts[1]);
    }

    private static Dictionary<string, string> ParseErrorFields(BackendMessage message)
    {
        Assert.Equal((byte)'E', message.Type);

        Dictionary<string, string> fields = new(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        while (index < message.Payload.Length && message.Payload[index] != 0)
        {
            var fieldCode = Encoding.ASCII.GetString(message.Payload, index, 1);
            index++;
            var end = Array.IndexOf(message.Payload, (byte)0, index);
            var value = Encoding.UTF8.GetString(message.Payload, index, end - index);
            fields[fieldCode] = value;
            index = end + 1;
        }

        return fields;
    }

    private static string ParseCommandComplete(BackendMessage message)
    {
        Assert.Equal((byte)'C', message.Type);
        return Encoding.UTF8.GetString(message.Payload).TrimEnd('\0');
    }

    private static byte[] CreateQueryPacket(string query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var payload = Encoding.UTF8.GetBytes(query + '\0');
        var packet = new byte[payload.Length + 5];
        packet[0] = (byte)'Q';
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(1, 4), payload.Length + 4);
        payload.CopyTo(packet.AsSpan(5));
        return packet;
    }

    private static byte[] CreateSslRequestPacket()
    {
        var packet = new byte[8];
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(0, 4), 8);
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(4, 4), SslRequestCode);
        return packet;
    }

    private static byte[] CreateStartupPacket(string user, string database, string applicationName = "SharpCoreDB.Tests")
    {
        var payload = new List<byte>();
        AppendInt32(payload, ProtocolVersion3);
        AppendCString(payload, "user");
        AppendCString(payload, user);
        AppendCString(payload, "database");
        AppendCString(payload, database);
        AppendCString(payload, "application_name");
        AppendCString(payload, applicationName);
        AppendCString(payload, "client_encoding");
        AppendCString(payload, "UTF8");
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
        ArgumentNullException.ThrowIfNull(value);
        buffer.AddRange(Encoding.UTF8.GetBytes(value));
        buffer.Add(0);
    }

    private readonly record struct BackendMessage(byte Type, byte[] Payload);
}
