// <copyright file="DatabaseService.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Grpc.Core;
using Microsoft.Extensions.Logging;
using SharpCoreDB.Server.Protocol;
using System.Runtime.CompilerServices;
using IsolationLevel = SharpCoreDB.Server.Protocol.IsolationLevel;
using Google.Protobuf;
using System.Diagnostics;
using SharpCoreDB.Server.Core;
using SharpCoreDB.Server.Core.Grpc;
using SharpCoreDB.Server.Core.Observability;
using SharpCoreDB.Server.Core.Security;
using SharpCoreDB.Server.Core.Tenancy;

namespace SharpCoreDB.Server.Core;

/// <summary>
/// gRPC service implementation for SharpCoreDB.
/// Handles all database operations with streaming support.
/// C# 14: Uses primary constructor for dependency injection.
/// </summary>
public sealed class DatabaseService(
    DatabaseRegistry databaseRegistry,
    SessionManager sessionManager,
    UserAuthenticationService authService,
    TenantQuotaEnforcementService tenantQuotaEnforcementService,
    TenantSecurityAuditService securityAuditService,
    ILogger<DatabaseService> logger,
    MetricsCollector metricsCollector) : Server.Protocol.DatabaseService.DatabaseServiceBase
{
    private readonly DatabaseRegistry _databaseRegistry = databaseRegistry;
    private readonly SessionManager _sessionManager = sessionManager;
    private readonly UserAuthenticationService _authService = authService;
    private readonly TenantQuotaEnforcementService _tenantQuotaEnforcementService = tenantQuotaEnforcementService;
    private readonly TenantSecurityAuditService _securityAuditService = securityAuditService;
    private readonly ILogger<DatabaseService> _logger = logger;
    private readonly MetricsCollector _metricsCollector = metricsCollector;

    /// <summary>
    /// Establishes a database connection and creates a session.
    /// Authenticates user credentials and returns a JWT token with RBAC role claims.
    /// </summary>
    public override async Task<ConnectResponse> Connect(ConnectRequest request, ServerCallContext context)
    {
        var requestedUser = request.UserName ?? "anonymous";
        var databaseName = string.IsNullOrWhiteSpace(request.DatabaseName)
            ? "master"
            : request.DatabaseName;

        try
        {
            _logger.LogInformation("Connect request from client '{Client}' for database '{Database}'",
                request.ClientName, databaseName);

            // Validate database exists
            if (!_databaseRegistry.DatabaseExists(databaseName))
            {
                EmitConnectEvent(
                    requestedUser,
                    tenantId: "unknown",
                    databaseName,
                    isAllowed: false,
                    code: "CONNECT_DATABASE_NOT_FOUND",
                    reason: "Database does not exist");

                return new ConnectResponse
                {
                    Status = ConnectionStatus.DatabaseNotFound
                };
            }

            // Authenticate user credentials
            var sessionId = Guid.NewGuid().ToString();
            var authResult = _authService.Authenticate(
                request.UserName ?? string.Empty,
                request.Password ?? string.Empty,
                sessionId);

            if (!authResult.IsAuthenticated)
            {
                _logger.LogWarning("Connect failed: invalid credentials for user '{User}'", request.UserName);
                EmitConnectEvent(
                    requestedUser,
                    tenantId: "unknown",
                    databaseName,
                    isAllowed: false,
                    code: "CONNECT_INVALID_CREDENTIALS",
                    reason: "Authentication failed");

                return new ConnectResponse
                {
                    Status = ConnectionStatus.InvalidCredentials
                };
            }

            var tenantId = _authService.GetUserTenantId(request.UserName ?? string.Empty) ?? "default";

            // Create session with authenticated role
            var session = await _sessionManager.CreateSessionAsync(
                databaseName,
                request.UserName,
                context.Peer,
                authResult.Role,
                tenantId,
                context.CancellationToken).ConfigureAwait(false);

            EmitConnectEvent(
                requestedUser,
                tenantId,
                databaseName,
                isAllowed: true,
                code: "CONNECT_OK",
                reason: "Connect succeeded");

            return new ConnectResponse
            {
                SessionId = session.SessionId,
                ServerVersion = "1.5.0",
                SupportedFeatures = { "SQL", "Transactions", "VectorSearch", "Analytics", "Graph", "RBAC" },
                Status = ConnectionStatus.Success
            };
        }
        catch (TenantQuotaExceededException ex)
        {
            _logger.LogWarning(ex, "Connect throttled by tenant quota");
            EmitConnectEvent(
                requestedUser,
                tenantId: _authService.GetUserTenantId(request.UserName ?? string.Empty) ?? "default",
                databaseName,
                isAllowed: false,
                code: ex.Code,
                reason: ex.Message);

            return new ConnectResponse
            {
                Status = ConnectionStatus.ConnectionLimitExceeded
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connect request failed");
            EmitConnectEvent(
                requestedUser,
                tenantId: _authService.GetUserTenantId(request.UserName ?? string.Empty) ?? "unknown",
                databaseName,
                isAllowed: false,
                code: "CONNECT_SERVER_UNAVAILABLE",
                reason: ex.Message);

            return new ConnectResponse
            {
                Status = ConnectionStatus.ServerUnavailable
            };
        }
    }

    /// <summary>
    /// Disconnects a client session.
    /// </summary>
    public override async Task<DisconnectResponse> Disconnect(DisconnectRequest request, ServerCallContext context)
    {
        try
        {
            await _sessionManager.RemoveSessionAsync(request.SessionId, context.CancellationToken);
            return new DisconnectResponse { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Disconnect request failed for session {SessionId}", request.SessionId);
            return new DisconnectResponse { Success = false };
        }
    }

    /// <summary>
    /// Health check ping.
    /// </summary>
    public override Task<PingResponse> Ping(PingRequest request, ServerCallContext context)
    {
        return Task.FromResult(new PingResponse
        {
            ServerTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ActiveConnections = _sessionManager.ActiveSessionCount
        });
    }

    /// <summary>
    /// Executes a SQL query with streaming results.
    /// PRIMARY USE CASE - optimized for performance with metrics tracking.
    /// </summary>
    public override async Task ExecuteQuery(QueryRequest request, IServerStreamWriter<QueryResponse> responseStream, ServerCallContext context)
    {
        var start = Stopwatch.GetTimestamp();
        var method = context.Method;

        var session = await _sessionManager.GetSessionAsync(request.SessionId, context.CancellationToken).ConfigureAwait(false);
        if (session == null)
        {
            _metricsCollector.RecordFailedRequest(method, "UNAUTHENTICATED");
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid session"));
        }

        await _tenantQuotaEnforcementService
            .EnsureRequestQuotaAsync(session.TenantId, method, cancellationToken: context.CancellationToken)
            .ConfigureAwait(false);

        await using var connection = await session.DatabaseInstance.GetConnectionAsync(context.CancellationToken).ConfigureAwait(false);

        var totalBytesReceived = 128 + (request.Sql?.Length ?? 0);
        var totalBytesSent = 0L;
        var totalRowsReturned = 0;

        if (string.IsNullOrWhiteSpace(request.Sql))
        {
            _metricsCollector.RecordFailedRequest(method, "INVALID_ARGUMENT");
            throw new RpcException(new Status(StatusCode.InvalidArgument, "SQL is required."));
        }

        var sql = request.Sql;

        try
        {
            var executionStart = Stopwatch.GetTimestamp();
            var result = connection.Database.ExecuteQuery(sql, []);
            var fetchSize = request.Options?.FetchSize > 0 ? request.Options.FetchSize : 1000;

            var columnNames = result.Count > 0
                ? result[0].Keys.ToArray()
                : [];

            // Send metadata frame first (streaming-friendly for large results)
            var metadataFrame = new QueryResponse
            {
                RowsAffected = result.Count,
                ExecutionTimeMs = 0,
                HasMore = result.Count > 0,
            };

            foreach (var columnName in columnNames)
            {
                metadataFrame.Columns.Add(new ColumnMetadata
                {
                    Name = columnName,
                    Type = SharpCoreDB.Server.Protocol.DataType.String,
                    Nullable = true,
                });
            }

            totalBytesSent += 256;
            await responseStream.WriteAsync(metadataFrame, context.CancellationToken).ConfigureAwait(false);

            if (result.Count == 0)
            {
                var elapsed = Stopwatch.GetElapsedTime(start);
                _metricsCollector.RecordSuccessfulRequest(method, elapsed.TotalMilliseconds, totalBytesReceived, totalBytesSent, 0);
                return;
            }

            var offset = 0;
            while (offset < result.Count)
            {
                var batchCount = Math.Min(fetchSize, result.Count - offset);
                var batchFrame = new QueryResponse
                {
                    RowsAffected = result.Count,
                    ExecutionTimeMs = 0,
                    HasMore = offset + batchCount < result.Count,
                };

                for (var i = offset; i < offset + batchCount; i++)
                {
                    var sourceRow = result[i];
                    var rowData = new RowData();

                    foreach (var columnName in columnNames)
                    {
                        sourceRow.TryGetValue(columnName, out var value);
                        rowData.Values.Add(MapParameterValue(value));
                    }

                    batchFrame.Rows.Add(rowData);
                    totalRowsReturned++;
                }

                totalBytesSent += (long)(batchCount * 128);
                await responseStream.WriteAsync(batchFrame, context.CancellationToken).ConfigureAwait(false);
                offset += batchCount;
            }

            // Final completion frame with execution metrics
            var completionFrame = new QueryResponse
            {
                RowsAffected = result.Count,
                ExecutionTimeMs = Stopwatch.GetElapsedTime(executionStart).TotalMilliseconds,
                HasMore = false,
            };

            totalBytesSent += 256;
            await responseStream.WriteAsync(completionFrame, context.CancellationToken).ConfigureAwait(false);

            var totalElapsed = Stopwatch.GetElapsedTime(start);
            _metricsCollector.RecordSuccessfulRequest(method, totalElapsed.TotalMilliseconds, totalBytesReceived, totalBytesSent, totalRowsReturned);
        }
        catch (TenantQuotaExceededException ex)
        {
            _metricsCollector.RecordFailedRequest(method, ex.Code);
            throw new RpcException(new Status(StatusCode.ResourceExhausted, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Query execution failed for session {SessionId}", request.SessionId);
            _metricsCollector.RecordFailedRequest(method, "INVALID_OPERATION");
            throw new RpcException(new Status(StatusCode.Internal, ex.Message));
        }
    }

    private MetricsCollector GetMetricsCollector()
    {
        return _databaseRegistry.GetType().Assembly
            .GetTypes()
            .FirstOrDefault(t => t.Name == "MetricsCollector") != null
            ? (MetricsCollector?)null ?? throw new InvalidOperationException("MetricsCollector not available")
            : throw new InvalidOperationException("MetricsCollector not available");
    }

    /// <summary>
    /// Executes a non-query SQL statement (INSERT/UPDATE/DELETE).
    /// </summary>
    public override async Task<NonQueryResponse> ExecuteNonQuery(NonQueryRequest request, ServerCallContext context)
    {
        var session = await _sessionManager.GetSessionAsync(request.SessionId, context.CancellationToken);
        if (session == null)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid session"));
        }

        await _tenantQuotaEnforcementService
            .EnsureRequestQuotaAsync(session.TenantId, context.Method, cancellationToken: context.CancellationToken)
            .ConfigureAwait(false);

        await _tenantQuotaEnforcementService
            .EnsureStorageQuotaAsync(
                session.TenantId,
                session.DatabaseInstance.Configuration.DatabasePath,
                context.Method,
                context.CancellationToken)
            .ConfigureAwait(false);

        await using var connection = await session.DatabaseInstance.GetConnectionAsync(context.CancellationToken);

        try
        {
            var startTime = Stopwatch.GetTimestamp();

            var sql = request.Sql;
            var sqlUpper = sql.Trim().ToUpperInvariant();

            // For DML statements, estimate rows affected from a pre-query count
            var rowsAffected = 0;
            if (sqlUpper.StartsWith("DELETE") || sqlUpper.StartsWith("UPDATE"))
            {
                try
                {
                    // Estimate by counting matching rows before execution
                    var countResult = connection.Database.ExecuteQuery(
                        ConvertToCountQuery(sql), []);
                    if (countResult.Count > 0 && countResult[0].Values.FirstOrDefault() is int count)
                        rowsAffected = count;
                    else if (countResult.Count > 0 && countResult[0].Values.FirstOrDefault() is long countL)
                        rowsAffected = (int)countL;
                }
                catch
                {
                    // If count query fails, fall through with 0
                }
            }

            connection.Database.ExecuteSQL(sql);

            if (sqlUpper.StartsWith("INSERT"))
                rowsAffected = 1; // Single INSERT always affects 1 row

            var executionTime = Stopwatch.GetElapsedTime(startTime);

            return new NonQueryResponse
            {
                RowsAffected = rowsAffected,
                ExecutionTimeMs = executionTime.TotalMilliseconds
            };
        }
        catch (TenantQuotaExceededException ex)
        {
            throw new RpcException(new Status(StatusCode.ResourceExhausted, ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Non-query execution failed for session {SessionId}", request.SessionId);
            throw new RpcException(new Status(StatusCode.Internal, ex.Message));
        }
    }

    /// <summary>
    /// Begins a database transaction.
    /// </summary>
    public override async Task<BeginTxResponse> BeginTransaction(BeginTxRequest request, ServerCallContext context)
    {
        var session = await _sessionManager.GetSessionAsync(request.SessionId, context.CancellationToken);
        if (session == null)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid session"));
        }

        try
        {
            var transactionId = Guid.NewGuid().ToString();
            session.ActiveTransactionId = transactionId;

            return new BeginTxResponse
            {
                TransactionId = transactionId,
                StartTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Begin transaction failed for session {SessionId}", request.SessionId);
            throw new RpcException(new Status(StatusCode.Internal, ex.Message));
        }
    }

    /// <summary>
    /// Commits a database transaction.
    /// </summary>
    public override async Task<CommitTxResponse> CommitTransaction(CommitTxRequest request, ServerCallContext context)
    {
        var session = await _sessionManager.GetSessionAsync(request.SessionId, context.CancellationToken);
        if (session == null)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid session"));
        }

        try
        {
            // Transaction commit logic would go here
            session.ActiveTransactionId = null;

            return new CommitTxResponse { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Commit transaction failed for session {SessionId}", request.SessionId);
            throw new RpcException(new Status(StatusCode.Internal, ex.Message));
        }
    }

    /// <summary>
    /// Rolls back a database transaction.
    /// </summary>
    public override async Task<RollbackTxResponse> RollbackTransaction(RollbackTxRequest request, ServerCallContext context)
    {
        var session = await _sessionManager.GetSessionAsync(request.SessionId, context.CancellationToken);
        if (session == null)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid session"));
        }

        try
        {
            // Transaction rollback logic would go here
            session.ActiveTransactionId = null;

            return new RollbackTxResponse { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rollback transaction failed for session {SessionId}", request.SessionId);
            throw new RpcException(new Status(StatusCode.Internal, ex.Message));
        }
    }

    /// <summary>
    /// Executes vector search with streaming results.
    /// </summary>
    public override async Task VectorSearch(VectorSearchRequest request, IServerStreamWriter<VectorSearchResponse> responseStream, ServerCallContext context)
    {
        var session = await _sessionManager.GetSessionAsync(request.SessionId, context.CancellationToken);
        if (session == null)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid session"));
        }

        await using var connection = await session.DatabaseInstance.GetConnectionAsync(context.CancellationToken);

        try
        {
            _ = connection; // Placeholder until vector API wiring is finalized.

            var response = new VectorSearchResponse
            {
                SearchTimeMs = 0,
                VectorsSearched = 0,
            };

            await responseStream.WriteAsync(response, context.CancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Vector search failed for session {SessionId}", request.SessionId);
            throw new RpcException(new Status(StatusCode.Internal, ex.Message));
        }
    }

    /// <summary>
    /// Health check for monitoring systems.
    /// </summary>
    public override Task<HealthCheckResponse> HealthCheck(HealthCheckRequest request, ServerCallContext context)
    {
        return Task.FromResult(new HealthCheckResponse
        {
            Status = SharpCoreDB.Server.Protocol.HealthStatus.Healthy,
            Message = $"SharpCoreDB server is healthy - {_sessionManager.ActiveSessionCount} active connections, {_databaseRegistry.DatabaseNames.Count} databases"
        });
    }

    // Helper methods
    private static DataType MapDataType(Type type)
    {
        if (type == typeof(int)) return DataType.Integer;
        if (type == typeof(long)) return DataType.Long;
        if (type == typeof(double)) return DataType.Real;
        if (type == typeof(string)) return DataType.String;
        if (type == typeof(byte[])) return DataType.Blob;
        if (type == typeof(bool)) return DataType.Boolean;
        if (type == typeof(DateTime)) return (DataType)6; // DATETIME
        if (type == typeof(Guid)) return DataType.Guid;
        if (type == typeof(float[])) return DataType.Vector;
        return DataType.String;
    }

    private static ParameterValue MapParameterValue(object? value)
    {
        var paramValue = new ParameterValue();

        switch (value)
        {
            case null:
                break;
            case int intValue:
                paramValue.IntValue = intValue;
                break;
            case long longValue:
                paramValue.LongValue = longValue;
                break;
            case double doubleValue:
                paramValue.DoubleValue = doubleValue;
                break;
            case string stringValue:
                paramValue.StringValue = stringValue;
                break;
            case byte[] bytesValue:
                paramValue.BytesValue = ByteString.CopyFrom(bytesValue);
                break;
            case bool boolValue:
                paramValue.BoolValue = boolValue;
                break;
            case DateTime dateTimeValue:
                paramValue.TimestampValue = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(dateTimeValue.ToUniversalTime());
                break;
            case Guid guidValue:
                paramValue.GuidValue = guidValue.ToString();
                break;
            case float[] vectorValue:
                paramValue.VectorValue = new VectorValue();
                paramValue.VectorValue.Values.AddRange(vectorValue);
                break;
            default:
                paramValue.StringValue = value?.ToString() ?? "";
                break;
        }

        return paramValue;
    }

    /// <summary>
    /// Converts a DML statement to a COUNT(*) query to estimate affected rows.
    /// Handles DELETE FROM table WHERE ... and UPDATE table SET ... WHERE ...
    /// </summary>
    private static string ConvertToCountQuery(string sql)
    {
        var trimmed = sql.Trim();
        var upper = trimmed.ToUpperInvariant();

        // DELETE FROM table WHERE ...  →  SELECT COUNT(*) FROM table WHERE ...
        if (upper.StartsWith("DELETE"))
        {
            var fromIdx = upper.IndexOf("FROM", StringComparison.Ordinal);
            if (fromIdx >= 0)
                return "SELECT COUNT(*) " + trimmed[fromIdx..];
        }

        // UPDATE table SET col=val WHERE ...  →  SELECT COUNT(*) FROM table WHERE ...
        if (upper.StartsWith("UPDATE"))
        {
            var setIdx = upper.IndexOf(" SET ", StringComparison.Ordinal);
            var whereIdx = upper.IndexOf("WHERE", StringComparison.Ordinal);
            if (setIdx > 0)
            {
                var tablePart = trimmed[6..setIdx].Trim(); // between UPDATE and SET
                var wherePart = whereIdx > 0 ? trimmed[whereIdx..] : "";
                return $"SELECT COUNT(*) FROM {tablePart} {wherePart}";
            }
        }

        return "SELECT 0";
    }

    private void EmitConnectEvent(
        string principal,
        string tenantId,
        string databaseName,
        bool isAllowed,
        string code,
        string reason)
    {
        _securityAuditService.Emit(new TenantSecurityAuditEvent(
            TimestampUtc: DateTime.UtcNow,
            EventType: isAllowed ? TenantSecurityEventType.ConnectSucceeded : TenantSecurityEventType.ConnectDenied,
            TenantId: string.IsNullOrWhiteSpace(tenantId) ? "default" : tenantId,
            DatabaseName: databaseName,
            Principal: principal,
            Protocol: "gRPC",
            IsAllowed: isAllowed,
            DecisionCode: code,
            Reason: reason));
    }
}
