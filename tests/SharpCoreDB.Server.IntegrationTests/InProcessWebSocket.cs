// <copyright file="InProcessWebSocket.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using System.Net.WebSockets;
using System.Threading.Channels;

namespace SharpCoreDB.Server.IntegrationTests;

/// <summary>
/// Creates a pair of connected in-memory WebSockets for testing.
/// Allows sending messages from one side and receiving on the other
/// without needing a real HTTP server.
/// C# 15: Channel-based async communication.
/// </summary>
public static class InProcessWebSocket
{
    /// <summary>
    /// Creates a client/server WebSocket pair connected via in-memory channels.
    /// </summary>
    public static (WebSocket Client, WebSocket Server) CreatePair()
    {
        var clientToServer = Channel.CreateUnbounded<WebSocketFrame>();
        var serverToClient = Channel.CreateUnbounded<WebSocketFrame>();

        var client = new ChannelWebSocket(serverToClient.Reader, clientToServer.Writer);
        var server = new ChannelWebSocket(clientToServer.Reader, serverToClient.Writer);

        return (client, server);
    }
}

/// <summary>
/// A frame sent through the in-memory channel.
/// </summary>
internal sealed record WebSocketFrame(
    ReadOnlyMemory<byte> Data,
    WebSocketMessageType MessageType,
    bool EndOfMessage,
    WebSocketCloseStatus? CloseStatus = null,
    string? CloseDescription = null);

/// <summary>
/// WebSocket implementation backed by <see cref="Channel{T}"/> for in-process testing.
/// </summary>
internal sealed class ChannelWebSocket(
    ChannelReader<WebSocketFrame> reader,
    ChannelWriter<WebSocketFrame> writer) : WebSocket
{
    private WebSocketState _state = WebSocketState.Open;
    private WebSocketCloseStatus? _closeStatus;
    private string? _closeDescription;

    public override WebSocketCloseStatus? CloseStatus => _closeStatus;
    public override string? CloseStatusDescription => _closeDescription;
    public override WebSocketState State => _state;
    public override string? SubProtocol => null;

    public override async Task SendAsync(
        ArraySegment<byte> buffer, WebSocketMessageType messageType,
        bool endOfMessage, CancellationToken cancellationToken)
    {
        if (_state != WebSocketState.Open)
        {
            throw new WebSocketException("WebSocket is not open");
        }

        var data = new byte[buffer.Count];
        Buffer.BlockCopy(buffer.Array!, buffer.Offset, data, 0, buffer.Count);

        await writer.WriteAsync(
            new WebSocketFrame(data, messageType, endOfMessage),
            cancellationToken);
    }

    public override async Task<WebSocketReceiveResult> ReceiveAsync(
        ArraySegment<byte> buffer, CancellationToken cancellationToken)
    {
        if (_state == WebSocketState.Closed || _state == WebSocketState.CloseReceived)
        {
            return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true,
                _closeStatus, _closeDescription);
        }

        var frame = await reader.ReadAsync(cancellationToken);

        if (frame.MessageType == WebSocketMessageType.Close)
        {
            _state = WebSocketState.CloseReceived;
            _closeStatus = frame.CloseStatus ?? WebSocketCloseStatus.NormalClosure;
            _closeDescription = frame.CloseDescription ?? "Closed";
            return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true,
                _closeStatus, _closeDescription);
        }

        frame.Data.Span.CopyTo(buffer.AsSpan());
        return new WebSocketReceiveResult(
            frame.Data.Length, frame.MessageType, frame.EndOfMessage);
    }

    public override async Task CloseAsync(
        WebSocketCloseStatus closeStatus, string? statusDescription,
        CancellationToken cancellationToken)
    {
        if (_state == WebSocketState.Closed) return;

        _state = WebSocketState.CloseSent;
        _closeStatus = closeStatus;
        _closeDescription = statusDescription;

        await writer.WriteAsync(
            new WebSocketFrame(ReadOnlyMemory<byte>.Empty, WebSocketMessageType.Close,
                true, closeStatus, statusDescription),
            cancellationToken);

        _state = WebSocketState.Closed;
        writer.TryComplete();
    }

    public override Task CloseOutputAsync(
        WebSocketCloseStatus closeStatus, string? statusDescription,
        CancellationToken cancellationToken)
    {
        return CloseAsync(closeStatus, statusDescription, cancellationToken);
    }

    public override void Abort()
    {
        _state = WebSocketState.Aborted;
        writer.TryComplete();
    }

    public override void Dispose()
    {
        if (_state != WebSocketState.Closed && _state != WebSocketState.Aborted)
        {
            _state = WebSocketState.Closed;
            writer.TryComplete();
        }
    }
}
