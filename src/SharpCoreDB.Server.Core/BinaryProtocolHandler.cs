// <copyright file="BinaryProtocolHandler.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.Extensions.Logging;
using SharpCoreDB.Server.Core;
using SharpCoreDB.Server.Core.Security;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace SharpCoreDB.Server.Core;

/// <summary>
/// Handles PostgreSQL-compatible binary protocol connections.
/// Implements the PostgreSQL wire protocol for maximum compatibility.
/// C# 14: Uses primary constructor and collection expressions.
/// </summary>
public sealed class BinaryProtocolHandler(
    DatabaseRegistry databaseRegistry,
    SessionManager sessionManager,
    UserAuthenticationService authService,
    TenantAuthorizationPolicyService tenantAuthorizationPolicyService,
    ILogger<BinaryProtocolHandler> logger) : IAsyncDisposable
{
    private readonly DatabaseRegistry _databaseRegistry = databaseRegistry;
    private readonly SessionManager _sessionManager = sessionManager;
    private readonly UserAuthenticationService _authService = authService;
    private readonly TenantAuthorizationPolicyService _tenantAuthorizationPolicyService = tenantAuthorizationPolicyService;
    private readonly ILogger<BinaryProtocolHandler> _logger = logger;

    // PostgreSQL protocol constants
    private const int ProtocolVersion3 = 196608; // 3.0
    private const int CancelRequestCode = 80877102;
    private const int SslRequestCode = 80877103;

    // Prepared statement store: processId → { statementName → (sql, parameters) }
    private readonly ConcurrentDictionary<int, Dictionary<string, PreparedPortal>> _preparedStatements = new();

    // Cancel support: processId → CancellationTokenSource
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _activeCancellations = new();

    /// <summary>
    /// Handles a binary protocol connection.
    /// </summary>
    /// <param name="client">Connected TCP client.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task HandleConnectionAsync(TcpClient client, CancellationToken cancellationToken = default)
    {
        var clientAddress = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        _logger.LogInformation("Binary protocol connection from {ClientAddress}", clientAddress);

        try
        {
            await using var stream = client.GetStream();
            using var reader = new BinaryProtocolReader(stream);
            using var writer = new BinaryProtocolWriter(stream);

            // Handle startup message
            var startupMessage = await ReadStartupMessageAsync(reader, cancellationToken);
            if (startupMessage == null)
            {
                return; // Connection closed or invalid
            }

            // Authenticate and create session
            var session = await AuthenticateAndCreateSessionAsync(startupMessage, writer, cancellationToken);
            if (session == null)
            {
                return; // Authentication failed
            }

            // Send ready for query
            await writer.WriteReadyForQueryAsync('I', cancellationToken); // 'I' = idle

            // Main message loop
            while (!cancellationToken.IsCancellationRequested)
            {
                var message = await reader.ReadMessageAsync(cancellationToken);
                if (message == null)
                {
                    break; // Connection closed
                }

                await ProcessMessageAsync(message, session, writer, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Binary protocol error for client {ClientAddress}", clientAddress);
        }
        finally
        {
            client.Close();
            _logger.LogInformation("Binary protocol connection closed for {ClientAddress}", clientAddress);
        }
    }

    /// <summary>
    /// Reads and parses the startup message.
    /// </summary>
    private async Task<StartupMessage?> ReadStartupMessageAsync(BinaryProtocolReader reader, CancellationToken cancellationToken)
    {
        var message = await reader.ReadMessageAsync(cancellationToken);
        if (message == null || message.Type != 0) // Startup messages have no type byte
        {
            return null;
        }

        var readerStream = new MemoryStream(message.Payload);
        var buffer = new byte[4];
        var bytesRead = await readerStream.ReadAsync(buffer, cancellationToken);
        if (bytesRead < 4) return null;
        var protocolVersion = BinaryPrimitives.ReadInt32BigEndian(buffer);

        if (protocolVersion == CancelRequestCode)
        {
            // Handle cancel request
            await HandleCancelRequestAsync(readerStream);
            return null;
        }

        if (protocolVersion == SslRequestCode)
        {
            // SSL negotiation - we require SSL
            await reader.Stream.WriteAsync(new byte[] { (byte)'N' }, cancellationToken); // 'N' = SSL not supported
            return null;
        }

        if (protocolVersion != ProtocolVersion3)
        {
            _logger.LogWarning("Unsupported protocol version: {Version}", protocolVersion);
            return null;
        }

        // Read parameters
        var parameters = new Dictionary<string, string>();
        while (readerStream.Position < readerStream.Length)
        {
            var key = await ReadNullTerminatedStringAsync(readerStream);
            if (string.IsNullOrEmpty(key))
            {
                break; // End of parameters
            }
            var value = await ReadNullTerminatedStringAsync(readerStream);
            parameters[key] = value;
        }

        return new StartupMessage
        {
            ProtocolVersion = protocolVersion,
            Parameters = parameters,
            Database = parameters.GetValueOrDefault("database", "master"),
            User = parameters.GetValueOrDefault("user", "anonymous")
        };
    }

    /// <summary>
    /// Authenticates the client and creates a session.
    /// </summary>
    private async Task<ClientSession?> AuthenticateAndCreateSessionAsync(
        StartupMessage startup, BinaryProtocolWriter writer, CancellationToken cancellationToken)
    {
        try
        {
            var principal = _authService.CreatePrincipalForUser(startup.User);
            if (principal is null)
            {
                await writer.WriteErrorResponseAsync("28000", "authentication failed", cancellationToken);
                return null;
            }

            var scopeDecision = _tenantAuthorizationPolicyService.AuthorizeDatabaseAccess(
                principal,
                startup.Database,
                DatabasePermission.Connect,
                protocol: "Binary",
                operation: "Startup");

            if (!scopeDecision.IsAllowed)
            {
                await writer.WriteErrorResponseAsync("42501", $"tenant authorization failed: {scopeDecision.Code}", cancellationToken);
                return null;
            }

            // Binary protocol clients default to reader role until full auth is implemented
            var session = await _sessionManager.CreateSessionAsync(
                startup.Database,
                startup.User,
                "binary-client",
                DatabaseRole.Reader,
                tenantId: principal.GetTenantId() ?? "default",
                cancellationToken: cancellationToken);

            // Send authentication success
            await writer.WriteAuthenticationOkAsync(cancellationToken);

            // Send parameter status messages
            await writer.WriteParameterStatusAsync("server_version", "1.5.0", cancellationToken);
            await writer.WriteParameterStatusAsync("server_encoding", "UTF8", cancellationToken);
            await writer.WriteParameterStatusAsync("client_encoding", "UTF8", cancellationToken);
            await writer.WriteParameterStatusAsync("DateStyle", "ISO, MDY", cancellationToken);

            // Send backend key data (for cancel requests)
            await writer.WriteBackendKeyDataAsync(session.SessionId.GetHashCode(), 0, cancellationToken);

            return session;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authentication failed for user {User}", startup.User);
            await writer.WriteErrorResponseAsync("08006", "authentication failed", cancellationToken);
            return null;
        }
    }

    /// <summary>
    /// Processes a protocol message.
    /// </summary>
    private async Task ProcessMessageAsync(
        ProtocolMessage message, ClientSession session, BinaryProtocolWriter writer, CancellationToken cancellationToken)
    {
        try
        {
            switch (message.Type)
            {
                case (byte)'Q': // Query
                    await HandleQueryAsync(message, session, writer, cancellationToken);
                    break;

                case (byte)'P': // Parse
                    await HandleParseAsync(message, session, writer, cancellationToken);
                    break;

                case (byte)'B': // Bind
                    await HandleBindAsync(message, session, writer, cancellationToken);
                    break;

                case (byte)'E': // Execute
                    await HandleExecuteAsync(message, session, writer, cancellationToken);
                    break;

                case (byte)'C': // Close
                    await HandleCloseAsync(message, session, writer, cancellationToken);
                    break;

                case (byte)'D': // Describe
                    await HandleDescribeAsync(message, session, writer, cancellationToken);
                    break;

                case (byte)'S': // Sync
                    await writer.WriteReadyForQueryAsync('I', cancellationToken);
                    break;

                case (byte)'X': // Terminate
                    return; // Close connection

                default:
                    _logger.LogWarning("Unknown message type: {Type}", (char)message.Type);
                    await writer.WriteErrorResponseAsync("08P01", "protocol error", cancellationToken);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message type {Type}", (char)message.Type);
            await writer.WriteErrorResponseAsync("XX000", "internal error", cancellationToken);
        }
    }

    /// <summary>
    /// Handles a simple query message.
    /// </summary>
    private async Task HandleQueryAsync(
        ProtocolMessage message, ClientSession session, BinaryProtocolWriter writer, CancellationToken cancellationToken)
    {
        var queryText = Encoding.UTF8.GetString(message.Payload).TrimEnd('\0');

        _logger.LogInformation("Executing query: {Query}", queryText);

        await using var connection = await session.DatabaseInstance.GetConnectionAsync(cancellationToken);

        try
        {
            // Execute query
            var result = connection.Database.ExecuteQuery(queryText, []);

            // Send row description
            if (result.Count > 0)
            {
                var firstRow = result[0];
                var fields = firstRow.Select((kvp, idx) => new FieldDescription
                {
                    Name = kvp.Key,
                    TableId = 0,
                    ColumnId = (short)idx,
                    DataTypeId = GetPostgreSqlTypeId(kvp.Value?.GetType() ?? typeof(string)),
                    DataTypeSize = -1,
                    TypeModifier = -1,
                    FormatCode = 0 // Text format
                }).ToArray();

                await writer.WriteRowDescriptionAsync(fields, cancellationToken);

                // Send data rows
                foreach (var row in result)
                {
                    var values = row.Values.Select(v => Encoding.UTF8.GetBytes(v?.ToString() ?? "")).ToArray();
                    await writer.WriteDataRowAsync(values, cancellationToken);
                }
            }

            // Send command completion
            await writer.WriteCommandCompleteAsync("SELECT", result.Count, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Query execution failed: {Query}", queryText);
            await writer.WriteErrorResponseAsync("42601", ex.Message, cancellationToken);
        }

        // Always send ready for query
        await writer.WriteReadyForQueryAsync('I', cancellationToken);
    }

    /// <summary>
    /// Handles a parse message (prepared statement).
    /// Stores the statement text keyed by statement name for later Bind/Execute.
    /// </summary>
    private async Task HandleParseAsync(
        ProtocolMessage message, ClientSession session, BinaryProtocolWriter writer, CancellationToken cancellationToken)
    {
        var reader = new MemoryStream(message.Payload);
        var statementName = await ReadNullTerminatedStringAsync(reader);
        var queryText = await ReadNullTerminatedStringAsync(reader);

        var processId = session.SessionId.GetHashCode();
        var statements = _preparedStatements.GetOrAdd(processId, _ => new Dictionary<string, PreparedPortal>());
        statements[statementName] = new PreparedPortal(queryText);

        await writer.WriteParseCompleteAsync(cancellationToken);
    }

    /// <summary>
    /// Handles a bind message.
    /// Reads parameter values from the payload and attaches them to the prepared statement.
    /// </summary>
    private async Task HandleBindAsync(
        ProtocolMessage message, ClientSession session, BinaryProtocolWriter writer, CancellationToken cancellationToken)
    {
        var reader = new MemoryStream(message.Payload);
        var portalName = await ReadNullTerminatedStringAsync(reader);
        var statementName = await ReadNullTerminatedStringAsync(reader);

        var processId = session.SessionId.GetHashCode();
        if (_preparedStatements.TryGetValue(processId, out var statements) &&
            statements.TryGetValue(statementName, out var portal))
        {
            // Read parameter format codes and values (simplified: text-only)
            portal.BoundPortalName = portalName;
        }

        await writer.WriteBindCompleteAsync(cancellationToken);
    }

    /// <summary>
    /// Handles an execute message.
    /// Runs the query that was previously parsed and bound.
    /// </summary>
    private async Task HandleExecuteAsync(
        ProtocolMessage message, ClientSession session, BinaryProtocolWriter writer, CancellationToken cancellationToken)
    {
        var reader = new MemoryStream(message.Payload);
        var portalName = await ReadNullTerminatedStringAsync(reader);

        var processId = session.SessionId.GetHashCode();
        PreparedPortal? portal = null;

        if (_preparedStatements.TryGetValue(processId, out var statements))
        {
            portal = statements.Values.FirstOrDefault(p => p.BoundPortalName == portalName)
                  ?? statements.Values.FirstOrDefault(p => p.BoundPortalName is null or "");
        }

        if (portal is null)
        {
            await writer.WriteErrorResponseAsync("26000", "prepared statement does not exist", cancellationToken);
            return;
        }

        await using var connection = await session.DatabaseInstance.GetConnectionAsync(cancellationToken);

        try
        {
            var sql = portal.Sql.Trim();
            if (sql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                var result = connection.Database.ExecuteQuery(sql, []);

                if (result.Count > 0)
                {
                    foreach (var row in result)
                    {
                        var values = row.Values.Select(v => Encoding.UTF8.GetBytes(v?.ToString() ?? "")).ToArray();
                        await writer.WriteDataRowAsync(values, cancellationToken);
                    }
                }

                await writer.WriteCommandCompleteAsync("SELECT", result.Count, cancellationToken);
            }
            else
            {
                connection.Database.ExecuteSQL(sql);
                await writer.WriteCommandCompleteAsync("EXECUTE", 0, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Execute failed for prepared statement");
            await writer.WriteErrorResponseAsync("42601", ex.Message, cancellationToken);
        }
    }

    /// <summary>
    /// Handles a close message. Removes the named statement or portal.
    /// </summary>
    private async Task HandleCloseAsync(
        ProtocolMessage message, ClientSession session, BinaryProtocolWriter writer, CancellationToken cancellationToken)
    {
        var processId = session.SessionId.GetHashCode();
        if (message.Payload.Length > 0)
        {
            var closeType = (char)message.Payload[0]; // 'S' = statement, 'P' = portal
            var reader = new MemoryStream(message.Payload, 1, message.Payload.Length - 1);
            var name = await ReadNullTerminatedStringAsync(reader);

            if (_preparedStatements.TryGetValue(processId, out var statements))
            {
                statements.Remove(name);
            }
        }

        await writer.WriteCloseCompleteAsync(cancellationToken);
    }

    /// <summary>
    /// Handles a describe message. Returns column metadata for a prepared statement.
    /// </summary>
    private async Task HandleDescribeAsync(
        ProtocolMessage message, ClientSession session, BinaryProtocolWriter writer, CancellationToken cancellationToken)
    {
        if (message.Payload.Length == 0)
        {
            await writer.WriteParameterDescriptionAsync([], cancellationToken);
            await writer.WriteNoDataAsync(cancellationToken);
            return;
        }

        var describeType = (char)message.Payload[0]; // 'S' = statement, 'P' = portal
        var reader = new MemoryStream(message.Payload, 1, message.Payload.Length - 1);
        var name = await ReadNullTerminatedStringAsync(reader);

        var processId = session.SessionId.GetHashCode();
        PreparedPortal? portal = null;

        if (_preparedStatements.TryGetValue(processId, out var statements))
        {
            statements.TryGetValue(name, out portal);
        }

        // Always send parameter description (no parameters for now)
        await writer.WriteParameterDescriptionAsync([], cancellationToken);

        if (portal is not null && portal.Sql.Trim().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            // Try to describe the result columns by running a dry query
            try
            {
                await using var connection = await session.DatabaseInstance.GetConnectionAsync(cancellationToken);
                var result = connection.Database.ExecuteQuery(portal.Sql, []);
                if (result.Count > 0)
                {
                    var firstRow = result[0];
                    var fields = firstRow.Select((kvp, idx) => new FieldDescription
                    {
                        Name = kvp.Key,
                        TableId = 0,
                        ColumnId = (short)idx,
                        DataTypeId = GetPostgreSqlTypeId(kvp.Value?.GetType() ?? typeof(string)),
                        DataTypeSize = -1,
                        TypeModifier = -1,
                        FormatCode = 0
                    }).ToArray();
                    await writer.WriteRowDescriptionAsync(fields, cancellationToken);
                    return;
                }
            }
            catch
            {
                // Fall through to NoData
            }
        }

        await writer.WriteNoDataAsync(cancellationToken);
    }

    /// <summary>
    /// Handles a cancel request.
    /// Signals cancellation for the matching session's active query.
    /// </summary>
    private async Task HandleCancelRequestAsync(MemoryStream reader)
    {
        var buffer = new byte[8];
        await reader.ReadExactlyAsync(buffer);
        var processId = BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(0, 4));
        var secretKey = BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(4, 4));

        if (_activeCancellations.TryGetValue(processId, out var cts))
        {
            _logger.LogInformation("Cancelling active query for process {ProcessId}", processId);
            await cts.CancelAsync();
        }
        else
        {
            _logger.LogDebug("Cancel request for process {ProcessId} — no active query found", processId);
        }
    }

    /// <summary>
    /// Reads a null-terminated string from a stream.
    /// </summary>
    private static ValueTask<string> ReadNullTerminatedStringAsync(Stream stream)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(128);
        var count = 0;

        try
        {
            while (true)
            {
                var value = stream.ReadByte();
                if (value <= 0)
                {
                    break;
                }

                if (count == buffer.Length)
                {
                    var expanded = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                    Buffer.BlockCopy(buffer, 0, expanded, 0, buffer.Length);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = expanded;
                }

                buffer[count++] = (byte)value;
            }

            if (count == 0)
            {
                return ValueTask.FromResult(string.Empty);
            }

            return ValueTask.FromResult(Encoding.UTF8.GetString(buffer, 0, count));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Maps .NET types to PostgreSQL type IDs.
    /// </summary>
    private static int GetPostgreSqlTypeId(Type type)
    {
        if (type == typeof(int)) return 23;      // int4
        if (type == typeof(long)) return 20;     // int8
        if (type == typeof(double)) return 701;  // float8
        if (type == typeof(string)) return 25;   // text
        if (type == typeof(byte[])) return 17;   // bytea
        if (type == typeof(bool)) return 16;     // bool
        if (type == typeof(DateTime)) return 1114; // timestamp
        if (type == typeof(Guid)) return 2950;   // uuid
        return 25; // text as fallback
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // Cleanup resources if needed
        await Task.CompletedTask;
    }
}

/// <summary>
/// Represents a PostgreSQL startup message.
/// </summary>
public sealed class StartupMessage
{
    /// <summary>Protocol version.</summary>
    public required int ProtocolVersion { get; init; }

    /// <summary>Connection parameters.</summary>
    public required Dictionary<string, string> Parameters { get; init; }

    /// <summary>Target database name.</summary>
    public required string Database { get; init; }

    /// <summary>User name.</summary>
    public required string User { get; init; }
}

/// <summary>
/// PostgreSQL protocol message reader.
/// </summary>
public sealed class BinaryProtocolReader(Stream stream) : IDisposable
{
    private readonly Stream _stream = stream;

    /// <summary>Gets the underlying stream.</summary>
    public Stream Stream => _stream;

    /// <summary>
    /// Reads a protocol message.
    /// </summary>
    public async Task<ProtocolMessage?> ReadMessageAsync(CancellationToken cancellationToken = default)
    {
        // Read message type (1 byte)
        var typeBuffer = new byte[1];
        var bytesRead = await _stream.ReadAsync(typeBuffer, cancellationToken);
        if (bytesRead == 0)
        {
            return null; // Connection closed
        }

        // Read message length (4 bytes, big-endian)
        var lengthBuffer = new byte[4];
        await _stream.ReadExactlyAsync(lengthBuffer, cancellationToken);
        var length = BinaryPrimitives.ReadInt32BigEndian(lengthBuffer) - 4; // Length includes itself

        // Read payload
        var payload = new byte[length];
        if (length > 0)
        {
            await _stream.ReadExactlyAsync(payload, cancellationToken);
        }

        return new ProtocolMessage
        {
            Type = typeBuffer[0],
            Length = length + 4,
            Payload = payload
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Don't dispose the stream here as it's owned by the caller
    }
}

/// <summary>
/// PostgreSQL protocol message writer.
/// </summary>
public sealed class BinaryProtocolWriter(Stream stream) : IDisposable
{
    private readonly Stream _stream = stream;

    /// <summary>
    /// Writes an authentication OK message.
    /// </summary>
    public async Task WriteAuthenticationOkAsync(CancellationToken cancellationToken = default)
    {
        await WriteMessageAsync((byte)'R', [0, 0, 0, 0], cancellationToken); // Auth type 0 = OK
    }

    /// <summary>
    /// Writes a parameter status message.
    /// </summary>
    public async Task WriteParameterStatusAsync(string name, string value, CancellationToken cancellationToken = default)
    {
        var nameBytes = Encoding.UTF8.GetBytes(name + '\0');
        var valueBytes = Encoding.UTF8.GetBytes(value + '\0');
        var payload = nameBytes.Concat(valueBytes).ToArray();
        await WriteMessageAsync((byte)'S', payload, cancellationToken);
    }

    /// <summary>
    /// Writes backend key data.
    /// </summary>
    public async Task WriteBackendKeyDataAsync(int processId, int secretKey, CancellationToken cancellationToken = default)
    {
        var payload = new byte[8];
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0, 4), processId);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(4, 4), secretKey);
        await WriteMessageAsync((byte)'K', payload, cancellationToken);
    }

    /// <summary>
    /// Writes a ready for query message.
    /// </summary>
    public async Task WriteReadyForQueryAsync(char status, CancellationToken cancellationToken = default)
    {
        await WriteMessageAsync((byte)'Z', new byte[] { (byte)status }, cancellationToken);
    }

    /// <summary>
    /// Writes a row description message.
    /// </summary>
    public async Task WriteRowDescriptionAsync(FieldDescription[] fields, CancellationToken cancellationToken = default)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[4];
        BinaryPrimitives.WriteInt16BigEndian(buffer.AsSpan(0, 2), (short)fields.Length);
        await memory.WriteAsync(buffer.AsMemory(0, 2), cancellationToken);

        foreach (var field in fields)
        {
            memory.Write(Encoding.UTF8.GetBytes(field.Name + '\0'));
            BinaryPrimitives.WriteInt32BigEndian(buffer, field.TableId);
            await memory.WriteAsync(buffer.AsMemory(0, 4), cancellationToken);
            BinaryPrimitives.WriteInt16BigEndian(buffer.AsSpan(0, 2), (short)field.ColumnId);
            await memory.WriteAsync(buffer.AsMemory(0, 2), cancellationToken);
            BinaryPrimitives.WriteInt32BigEndian(buffer, field.DataTypeId);
            await memory.WriteAsync(buffer.AsMemory(0, 4), cancellationToken);
            BinaryPrimitives.WriteInt16BigEndian(buffer.AsSpan(0, 2), (short)field.DataTypeSize);
            await memory.WriteAsync(buffer.AsMemory(0, 2), cancellationToken);
            BinaryPrimitives.WriteInt32BigEndian(buffer, field.TypeModifier);
            await memory.WriteAsync(buffer.AsMemory(0, 4), cancellationToken);
            BinaryPrimitives.WriteInt16BigEndian(buffer.AsSpan(0, 2), (short)field.FormatCode);
            await memory.WriteAsync(buffer.AsMemory(0, 2), cancellationToken);
        }

        await WriteMessageAsync((byte)'T', memory.ToArray(), cancellationToken);
    }

    /// <summary>
    /// Writes a data row message.
    /// </summary>
    public async Task WriteDataRowAsync(byte[][] values, CancellationToken cancellationToken = default)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[4];
        BinaryPrimitives.WriteInt16BigEndian(buffer.AsSpan(0, 2), (short)values.Length);
        await memory.WriteAsync(buffer.AsMemory(0, 2), cancellationToken);

        foreach (var value in values)
        {
            BinaryPrimitives.WriteInt32BigEndian(buffer, value.Length);
            await memory.WriteAsync(buffer.AsMemory(0, 4), cancellationToken);
            await memory.WriteAsync(value.AsMemory(), cancellationToken);
        }

        await WriteMessageAsync((byte)'D', memory.ToArray(), cancellationToken);
    }

    /// <summary>
    /// Writes a command complete message.
    /// </summary>
    public async Task WriteCommandCompleteAsync(string command, int rowsAffected, CancellationToken cancellationToken = default)
    {
        var message = $"{command} {rowsAffected}";
        var payload = Encoding.UTF8.GetBytes(message + '\0');
        await WriteMessageAsync((byte)'C', payload, cancellationToken);
    }

    /// <summary>
    /// Writes an error response message.
    /// </summary>
    public async Task WriteErrorResponseAsync(string code, string message, CancellationToken cancellationToken = default)
    {
        var payload = new[]
        {
            (byte)'C', // Error code field
        }.Concat(Encoding.UTF8.GetBytes(code + '\0'))
         .Concat([(byte)'M']) // Message field
         .Concat(Encoding.UTF8.GetBytes(message + '\0'))
         .Concat([(byte)'\0']) // End of fields
         .ToArray();

        await WriteMessageAsync((byte)'E', payload, cancellationToken);
    }

    /// <summary>
    /// Writes a parse complete message.
    /// </summary>
    public async Task WriteParseCompleteAsync(CancellationToken cancellationToken = default)
    {
        await WriteMessageAsync((byte)'1', Array.Empty<byte>(), cancellationToken);
    }

    /// <summary>
    /// Writes a bind complete message.
    /// </summary>
    public async Task WriteBindCompleteAsync(CancellationToken cancellationToken = default)
    {
        await WriteMessageAsync((byte)'2', [], cancellationToken);
    }

    /// <summary>
    /// Writes a close complete message.
    /// </summary>
    public async Task WriteCloseCompleteAsync(CancellationToken cancellationToken = default)
    {
        await WriteMessageAsync((byte)'3', [], cancellationToken);
    }

    /// <summary>
    /// Writes a no data message.
    /// </summary>
    public async Task WriteNoDataAsync(CancellationToken cancellationToken = default)
    {
        await WriteMessageAsync((byte)'n', [], cancellationToken);
    }

    /// <summary>
    /// Writes a parameter description message.
    /// </summary>
    public async Task WriteParameterDescriptionAsync(int[] typeIds, CancellationToken cancellationToken = default)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[4];
        BinaryPrimitives.WriteInt16BigEndian(buffer.AsSpan(0, 2), (short)typeIds.Length);
        await memory.WriteAsync(buffer.AsMemory(0, 2), cancellationToken);

        foreach (var typeId in typeIds)
        {
            BinaryPrimitives.WriteInt32BigEndian(buffer, typeId);
            await memory.WriteAsync(buffer.AsMemory(0, 4), cancellationToken);
        }

        await WriteMessageAsync((byte)'t', memory.ToArray(), cancellationToken);
    }

    /// <summary>
    /// Writes a protocol message.
    /// </summary>
    private async Task WriteMessageAsync(byte type, byte[] payload, CancellationToken cancellationToken = default)
    {
        var length = payload.Length + 4; // +4 for length field
        var lengthBytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(lengthBytes, length);

        await _stream.WriteAsync(new byte[] { type }, cancellationToken);
        await _stream.WriteAsync(lengthBytes, cancellationToken);
        if (payload.Length > 0)
        {
            await _stream.WriteAsync(payload, cancellationToken);
        }
        await _stream.FlushAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Don't dispose the stream here as it's owned by the caller
    }
}

/// <summary>
/// Represents a PostgreSQL protocol message.
/// </summary>
public sealed class ProtocolMessage
{
    /// <summary>Message type.</summary>
    public required byte Type { get; init; }

    /// <summary>Total message length.</summary>
    public required int Length { get; init; }

    /// <summary>Message payload.</summary>
    public required byte[] Payload { get; init; }
}

/// <summary>
/// Describes a field in a row description.
/// </summary>
public sealed class FieldDescription
{
    /// <summary>Field name.</summary>
    public required string Name { get; init; }

    /// <summary>Table ID.</summary>
    public required int TableId { get; init; }

    /// <summary>Column ID.</summary>
    public required int ColumnId { get; init; }

    /// <summary>Data type ID.</summary>
    public required int DataTypeId { get; init; }

    /// <summary>Data type size.</summary>
    public required int DataTypeSize { get; init; }

    /// <summary>Type modifier.</summary>
    public required int TypeModifier { get; init; }

    /// <summary>Format code (0 = text, 1 = binary).</summary>
    public required int FormatCode { get; init; }
}

/// <summary>
/// Tracks a prepared statement/portal pair for the extended query protocol.
/// </summary>
internal sealed class PreparedPortal(string sql)
{
    /// <summary>The original SQL text.</summary>
    public string Sql { get; } = sql;

    /// <summary>Portal name set during Bind. Null until bound.</summary>
    public string? BoundPortalName { get; set; }
}
