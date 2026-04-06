// <copyright file="WebSocketHandler.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpCoreDB.Server.Core.Security;
using System.Buffers;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using SysWsMessageType = System.Net.WebSockets.WebSocketMessageType;

namespace SharpCoreDB.Server.Core.WebSockets;

/// <summary>
/// Handles a single WebSocket connection.
/// Authenticates via JWT, executes SQL, and streams results as JSON frames.
/// C# 14: Primary constructor, pattern matching, async streams.
/// </summary>
public sealed class WebSocketHandler(
    DatabaseRegistry databaseRegistry,
    SessionManager sessionManager,
    JwtTokenService tokenService,
    RbacService rbacService,
    IOptions<ServerConfiguration> configuration,
    ILogger<WebSocketHandler> logger) : IAsyncDisposable
{
    private readonly DatabaseRegistry _databaseRegistry = databaseRegistry;
    private readonly SessionManager _sessionManager = sessionManager;
    private readonly JwtTokenService _tokenService = tokenService;
    private readonly RbacService _rbacService = rbacService;
    private readonly ServerConfiguration _config = configuration.Value;
    private readonly ILogger<WebSocketHandler> _logger = logger;

    private ClaimsPrincipal? _authenticatedPrincipal;
    private ClientSession? _session;
    private bool _disposed;

    /// <summary>
    /// Processes a WebSocket connection, handling messages until the client disconnects.
    /// </summary>
    /// <param name="webSocket">The accepted WebSocket.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task HandleAsync(WebSocket webSocket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(webSocket);

        _logger.LogInformation("WebSocket connection accepted");

        var buffer = ArrayPool<byte>.Shared.Rent(_config.WebSocketMaxMessageSize);
        try
        {
            while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await ReceiveFullMessageAsync(webSocket, buffer, cancellationToken)
                    .ConfigureAwait(false);

                if (result.MessageType == SysWsMessageType.Close)
                {
                    await webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure, "Goodbye",
                        cancellationToken).ConfigureAwait(false);
                    break;
                }

                if (result.MessageType != SysWsMessageType.Text || result.Count == 0)
                {
                    continue;
                }

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                await ProcessMessageAsync(webSocket, json, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
        {
            _logger.LogDebug("WebSocket client disconnected");
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("WebSocket connection cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WebSocket handler error");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            if (_session is not null)
            {
                await _sessionManager.RemoveSessionAsync(_session.SessionId, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            _logger.LogInformation("WebSocket connection closed");
        }
    }

    private async Task ProcessMessageAsync(
        WebSocket webSocket, string json, CancellationToken cancellationToken)
    {
        WebSocketRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<WebSocketRequest>(json, WebSocketJsonOptions.Default);
        }
        catch (JsonException ex)
        {
            await SendErrorAsync(webSocket, "unknown", "PARSE_ERROR", $"Invalid JSON: {ex.Message}",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (request is null)
        {
            await SendErrorAsync(webSocket, "unknown", "PARSE_ERROR", "Empty message",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        // Auth messages are always allowed
        if (request.Type == WebSockets.WebSocketMessageType.Auth)
        {
            await HandleAuthAsync(webSocket, request, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (request.Type == WebSockets.WebSocketMessageType.Ping)
        {
            await SendResponseAsync(webSocket, new WebSocketResponse
            {
                Type = WebSockets.WebSocketMessageType.Pong,
                Id = request.Id,
                Success = true,
                Message = "pong",
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        // All other messages require authentication
        if (_authenticatedPrincipal is null || _session is null)
        {
            await SendErrorAsync(webSocket, request.Id, "UNAUTHENTICATED",
                "Send an Auth message with a valid JWT token first",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        switch (request.Type)
        {
            case WebSockets.WebSocketMessageType.Query:
                await HandleQueryAsync(webSocket, request, cancellationToken).ConfigureAwait(false);
                break;

            case WebSockets.WebSocketMessageType.Execute:
                await HandleExecuteAsync(webSocket, request, cancellationToken).ConfigureAwait(false);
                break;

            case WebSockets.WebSocketMessageType.Batch:
                await HandleBatchAsync(webSocket, request, cancellationToken).ConfigureAwait(false);
                break;

            default:
                await SendErrorAsync(webSocket, request.Id, "UNSUPPORTED",
                    $"Message type '{request.Type}' is not supported",
                    cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private async Task HandleAuthAsync(
        WebSocket webSocket, WebSocketRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            await SendErrorAsync(webSocket, request.Id, "AUTH_FAILED",
                "Token is required", cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            var principal = _tokenService.ValidateToken(request.Token);
            var username = _tokenService.GetUsernameFromToken(principal);
            var sessionId = _tokenService.GetSessionIdFromToken(principal);

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(sessionId))
            {
                await SendErrorAsync(webSocket, request.Id, "AUTH_FAILED",
                    "Token missing required claims", cancellationToken).ConfigureAwait(false);
                return;
            }

            _authenticatedPrincipal = principal;

            // Create a session for this WebSocket connection
            var databaseName = request.Database ?? _config.DefaultDatabase;
            var role = RbacService.GetRoleFromPrincipal(principal);
            _session = await _sessionManager.CreateSessionAsync(
                databaseName,
                username,
                "websocket",
                role,
                tenantId: principal.GetTenantId() ?? "default",
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "WebSocket authenticated: User={Username}, Session={SessionId}, Role={Role}",
                username, _session.SessionId, role);

            await SendResponseAsync(webSocket, new WebSocketResponse
            {
                Type = WebSockets.WebSocketMessageType.Ack,
                Id = request.Id,
                Success = true,
                Message = $"Authenticated as {username} (role={role})",
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WebSocket auth failed");
            await SendErrorAsync(webSocket, request.Id, "AUTH_FAILED",
                "Token validation failed", cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleQueryAsync(
        WebSocket webSocket, WebSocketRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Sql))
        {
            await SendErrorAsync(webSocket, request.Id, "INVALID_REQUEST",
                "SQL is required for Query", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!_rbacService.AuthorizeGrpcCall(_authenticatedPrincipal!, "/sharpcoredb.v1.DatabaseService/ExecuteQuery"))
        {
            await SendErrorAsync(webSocket, request.Id, "PERMISSION_DENIED",
                "Insufficient permissions for query execution", cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            var start = Stopwatch.GetTimestamp();
            await using var connection = await _session!.DatabaseInstance
                .GetConnectionAsync(cancellationToken).ConfigureAwait(false);

            var result = connection.Database.ExecuteQuery(request.Sql, []);
            var columns = result.Count > 0 ? result[0].Keys.ToList() : [];

            // Stream results in batches of 1000 rows
            const int batchSize = 1000;
            var offset = 0;

            while (offset < result.Count)
            {
                var count = Math.Min(batchSize, result.Count - offset);
                var rows = new List<Dictionary<string, object?>>(count);

                for (var i = offset; i < offset + count; i++)
                {
                    rows.Add(result[i].ToDictionary(static kvp => kvp.Key, static kvp => (object?)kvp.Value));
                }

                var hasMore = offset + count < result.Count;

                await SendResponseAsync(webSocket, new WebSocketResponse
                {
                    Type = WebSockets.WebSocketMessageType.Result,
                    Id = request.Id,
                    Success = true,
                    Columns = offset == 0 ? columns : null,
                    Rows = rows,
                    HasMore = hasMore,
                }, cancellationToken).ConfigureAwait(false);

                offset += count;
            }

            // Empty result set
            if (result.Count == 0)
            {
                await SendResponseAsync(webSocket, new WebSocketResponse
                {
                    Type = WebSockets.WebSocketMessageType.Result,
                    Id = request.Id,
                    Success = true,
                    Columns = columns,
                    Rows = [],
                    HasMore = false,
                }, cancellationToken).ConfigureAwait(false);
            }

            _logger.LogDebug("WebSocket query completed: {RowCount} rows in {Elapsed}ms",
                result.Count, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WebSocket query failed");
            await SendErrorAsync(webSocket, request.Id, "QUERY_ERROR",
                ex.Message, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleExecuteAsync(
        WebSocket webSocket, WebSocketRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Sql))
        {
            await SendErrorAsync(webSocket, request.Id, "INVALID_REQUEST",
                "SQL is required for Execute", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!_rbacService.AuthorizeGrpcCall(_authenticatedPrincipal!, "/sharpcoredb.v1.DatabaseService/ExecuteNonQuery"))
        {
            await SendErrorAsync(webSocket, request.Id, "PERMISSION_DENIED",
                "Insufficient permissions for execute", cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            await using var connection = await _session!.DatabaseInstance
                .GetConnectionAsync(cancellationToken).ConfigureAwait(false);

            connection.Database.ExecuteSQL(request.Sql);

            await SendResponseAsync(webSocket, new WebSocketResponse
            {
                Type = WebSockets.WebSocketMessageType.Ack,
                Id = request.Id,
                Success = true,
                Message = "Statement executed",
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WebSocket execute failed");
            await SendErrorAsync(webSocket, request.Id, "EXECUTE_ERROR",
                ex.Message, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleBatchAsync(
        WebSocket webSocket, WebSocketRequest request, CancellationToken cancellationToken)
    {
        if (request.Statements is null || request.Statements.Count == 0)
        {
            await SendErrorAsync(webSocket, request.Id, "INVALID_REQUEST",
                "Statements list is required for Batch", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!_rbacService.AuthorizeGrpcCall(_authenticatedPrincipal!, "/sharpcoredb.v1.DatabaseService/ExecuteBatch"))
        {
            await SendErrorAsync(webSocket, request.Id, "PERMISSION_DENIED",
                "Insufficient permissions for batch execution", cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            await using var connection = await _session!.DatabaseInstance
                .GetConnectionAsync(cancellationToken).ConfigureAwait(false);

            connection.Database.ExecuteBatchSQL(request.Statements);

            await SendResponseAsync(webSocket, new WebSocketResponse
            {
                Type = WebSockets.WebSocketMessageType.Ack,
                Id = request.Id,
                Success = true,
                AffectedRows = request.Statements.Count,
                Message = $"Batch executed ({request.Statements.Count} statements)",
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WebSocket batch failed");
            await SendErrorAsync(webSocket, request.Id, "BATCH_ERROR",
                ex.Message, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task SendResponseAsync(
        WebSocket webSocket, WebSocketResponse response, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(response, WebSocketJsonOptions.Default);
        await webSocket.SendAsync(
            json.AsMemory(), SysWsMessageType.Text,
            endOfMessage: true, cancellationToken).ConfigureAwait(false);
    }

    private static async Task SendErrorAsync(
        WebSocket webSocket, string requestId, string code, string error,
        CancellationToken cancellationToken)
    {
        var response = new WebSocketResponse
        {
            Type = WebSockets.WebSocketMessageType.Error,
            Id = requestId,
            Success = false,
            Error = error,
            Code = code,
        };

        await SendResponseAsync(webSocket, response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Receives a complete WebSocket message, handling fragmented frames.
    /// </summary>
    private static async Task<(int Count, SysWsMessageType MessageType)> ReceiveFullMessageAsync(
        WebSocket webSocket, byte[] buffer, CancellationToken cancellationToken)
    {
        var totalBytes = 0;
        ValueWebSocketReceiveResult result;

        do
        {
            result = await webSocket.ReceiveAsync(
                buffer.AsMemory(totalBytes), cancellationToken).ConfigureAwait(false);
            totalBytes += result.Count;

            if (totalBytes >= buffer.Length && !result.EndOfMessage)
            {
                throw new InvalidOperationException(
                    $"WebSocket message exceeds maximum size ({buffer.Length} bytes)");
            }
        }
        while (!result.EndOfMessage);

        return (totalBytes, result.MessageType);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_session is not null)
        {
            await _sessionManager.RemoveSessionAsync(_session.SessionId, CancellationToken.None)
                .ConfigureAwait(false);
        }
    }
}
