// <copyright file="DatabaseController.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharpCoreDB.Server.Core;
using SharpCoreDB.Server.Core.Observability;
using SharpCoreDB.Server.Core.Security;
using SharpCoreDB.Server.Core.Tenancy;
using System.Diagnostics;

namespace SharpCoreDB.Server;

/// <summary>
/// REST API controller for SharpCoreDB operations (tertiary protocol).
/// Provides HTTP/JSON endpoints for database access.
/// C# 14: Primary constructor, pattern matching, collection expressions.
/// </summary>
[ApiController]
[Route("api/v1")]
[Authorize]
public sealed class DatabaseController(
    DatabaseRegistry databaseRegistry,
    SessionManager sessionManager,
    UserAuthenticationService authService,
    TenantAccessAuditStore tenantAccessAuditStore,
    TenantSecurityAuditStore tenantSecurityAuditStore,
    TenantAuthorizationPolicyService tenantAuthorizationPolicyService,
    TenantQuotaEnforcementService tenantQuotaEnforcementService,
    MetricsCollector metricsCollector,
    HealthCheckService healthCheckService,
    ILogger<DatabaseController> logger) : ControllerBase
{
    private static readonly Stopwatch ServerUptime = Stopwatch.StartNew();

    /// <summary>
    /// Executes a SQL query and returns results as JSON.
    /// </summary>
    [HttpPost("query")]
    [Authorize(Roles = "admin,writer,reader")]
    [ProducesResponseType(typeof(QueryResponse), 200)]
    [ProducesResponseType(typeof(ErrorResponse), 400)]
    [ProducesResponseType(typeof(ErrorResponse), 500)]
    public async Task<IActionResult> ExecuteQueryAsync(
        [FromBody] QueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var start = Stopwatch.GetTimestamp();

        if (string.IsNullOrWhiteSpace(request.Sql))
        {
            return BadRequest(new ErrorResponse { Error = "SQL is required", Code = "INVALID_SQL", Details = "Query SQL cannot be empty" });
        }

        var (db, session, error) = await ResolveSessionAsync(request.Database, DatabasePermission.Select, cancellationToken);
        if (error is not null) return error;

        try
        {
            await tenantQuotaEnforcementService
                .EnsureRequestQuotaAsync(session!.TenantId, "REST/query", cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            await using var connection = await db!.GetConnectionAsync(cancellationToken);
            var result = connection.Database.ExecuteQuery(request.Sql, request.Parameters ?? []);

            var columns = Array.Empty<ColumnInfo>();
            var rows = Array.Empty<object?[]>();

            if (result.Count > 0)
            {
                var keys = result[0].Keys.ToArray();
                columns = keys.Select(name => new ColumnInfo
                {
                    Name = name,
                    Type = result[0][name]?.GetType().Name ?? "string",
                    Nullable = true,
                }).ToArray();

                rows = result.Select(row =>
                    keys.Select(k => row.GetValueOrDefault(k)).ToArray()
                ).ToArray();
            }

            var elapsed = Stopwatch.GetElapsedTime(start);
            metricsCollector.RecordSuccessfulRequest("REST/query", elapsed.TotalMilliseconds, request.Sql.Length, 0, result.Count);

            return Ok(new QueryResponse
            {
                Columns = columns,
                Rows = rows,
                RowsAffected = result.Count,
                ExecutionTimeMs = elapsed.TotalMilliseconds,
            });
        }
        catch (TenantQuotaExceededException ex)
        {
            metricsCollector.RecordFailedRequest("REST/query", ex.Code);
            return StatusCode(429, new ErrorResponse { Error = ex.Message, Code = ex.Code, Details = "Tenant quota exceeded" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "REST query failed: {Sql}", request.Sql);
            metricsCollector.RecordFailedRequest("REST/query", "QUERY_ERROR");
            return StatusCode(500, new ErrorResponse { Error = "Query execution failed", Code = "QUERY_ERROR", Details = ex.Message });
        }
        finally
        {
            await sessionManager.RemoveSessionAsync(session!.SessionId, cancellationToken);
        }
    }

    /// <summary>
    /// Executes a non-query SQL statement (INSERT/UPDATE/DELETE/CREATE).
    /// </summary>
    [HttpPost("execute")]
    [Authorize(Roles = "admin,writer")]
    [ProducesResponseType(typeof(ExecuteResponse), 200)]
    [ProducesResponseType(typeof(ErrorResponse), 400)]
    [ProducesResponseType(typeof(ErrorResponse), 500)]
    public async Task<IActionResult> ExecuteNonQueryAsync(
        [FromBody] ExecuteRequest request,
        CancellationToken cancellationToken = default)
    {
        var start = Stopwatch.GetTimestamp();

        var (db, session, error) = await ResolveSessionAsync(request.Database, DatabasePermission.Insert, cancellationToken);
        if (error is not null) return error;

        try
        {
            await tenantQuotaEnforcementService
                .EnsureRequestQuotaAsync(session!.TenantId, "REST/execute", cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            await tenantQuotaEnforcementService
                .EnsureStorageQuotaAsync(
                    session.TenantId,
                    session.DatabaseInstance.Configuration.DatabasePath,
                    "REST/execute",
                    cancellationToken)
                .ConfigureAwait(false);

            await using var connection = await db!.GetConnectionAsync(cancellationToken);
            connection.Database.ExecuteSQL(request.Sql);
            var elapsed = Stopwatch.GetElapsedTime(start);

            metricsCollector.RecordSuccessfulRequest("REST/execute", elapsed.TotalMilliseconds, request.Sql.Length, 0);

            return Ok(new ExecuteResponse
            {
                RowsAffected = 0,
                ExecutionTimeMs = elapsed.TotalMilliseconds,
            });
        }
        catch (TenantQuotaExceededException ex)
        {
            metricsCollector.RecordFailedRequest("REST/execute", ex.Code);
            return StatusCode(429, new ErrorResponse { Error = ex.Message, Code = ex.Code, Details = "Tenant quota exceeded" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "REST execute failed: {Sql}", request.Sql);
            metricsCollector.RecordFailedRequest("REST/execute", "EXECUTE_ERROR");
            return StatusCode(500, new ErrorResponse { Error = "Execute failed", Code = "EXECUTE_ERROR", Details = ex.Message });
        }
        finally
        {
            await sessionManager.RemoveSessionAsync(session!.SessionId, cancellationToken);
        }
    }

    /// <summary>
    /// Executes a batch of SQL statements using ExecuteBatchSQL for storage persistence.
    /// </summary>
    [HttpPost("batch")]
    [Authorize(Roles = "admin,writer")]
    [ProducesResponseType(typeof(BatchResponse), 200)]
    [ProducesResponseType(typeof(ErrorResponse), 400)]
    [ProducesResponseType(typeof(ErrorResponse), 500)]
    public async Task<IActionResult> ExecuteBatchAsync(
        [FromBody] BatchRequest request,
        CancellationToken cancellationToken = default)
    {
        var start = Stopwatch.GetTimestamp();

        if (request.Statements is not { Length: > 0 })
        {
            return BadRequest(new ErrorResponse { Error = "No statements", Code = "EMPTY_BATCH", Details = "Batch requires at least one statement" });
        }

        var (db, session, error) = await ResolveSessionAsync(request.Database, DatabasePermission.Insert, cancellationToken);
        if (error is not null) return error;

        try
        {
            await tenantQuotaEnforcementService
                .EnsureRequestQuotaAsync(
                    session!.TenantId,
                    "REST/batch",
                    batchSize: request.Statements.Length,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            await tenantQuotaEnforcementService
                .EnsureStorageQuotaAsync(
                    session.TenantId,
                    session.DatabaseInstance.Configuration.DatabasePath,
                    "REST/batch",
                    cancellationToken)
                .ConfigureAwait(false);

            await using var connection = await db!.GetConnectionAsync(cancellationToken);
            connection.Database.ExecuteBatchSQL(request.Statements);
            var elapsed = Stopwatch.GetElapsedTime(start);

            metricsCollector.RecordSuccessfulRequest("REST/batch", elapsed.TotalMilliseconds, 0, 0);

            return Ok(new BatchResponse
            {
                StatementsExecuted = request.Statements.Length,
                TotalExecutionTimeMs = elapsed.TotalMilliseconds,
                Success = true,
            });
        }
        catch (TenantQuotaExceededException ex)
        {
            metricsCollector.RecordFailedRequest("REST/batch", ex.Code);
            return StatusCode(429, new ErrorResponse { Error = ex.Message, Code = ex.Code, Details = "Tenant quota exceeded" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "REST batch failed ({Count} statements)", request.Statements.Length);
            metricsCollector.RecordFailedRequest("REST/batch", "BATCH_ERROR");
            return StatusCode(500, new ErrorResponse { Error = "Batch execution failed", Code = "BATCH_ERROR", Details = ex.Message });
        }
        finally
        {
            await sessionManager.RemoveSessionAsync(session!.SessionId, cancellationToken);
        }
    }

    /// <summary>
    /// Gets database schema information (tables and columns).
    /// </summary>
    [HttpGet("schema")]
    [ProducesResponseType(typeof(SchemaResponse), 200)]
    [ProducesResponseType(typeof(ErrorResponse), 400)]
    public async Task<IActionResult> GetSchemaAsync(
        [FromQuery] string? database = null,
        CancellationToken cancellationToken = default)
    {
        var databaseName = database ?? "master";
        var db = databaseRegistry.GetDatabase(databaseName);
        if (db is null)
        {
            return BadRequest(new ErrorResponse { Error = "Database not found", Code = "DATABASE_NOT_FOUND", Details = $"Database '{databaseName}' does not exist" });
        }

        try
        {
            await using var connection = await db.GetConnectionAsync(cancellationToken);

            // Query actual table list from SharpCoreDB metadata
            var dbTables = connection.Database.GetTables();
            var tableInfos = dbTables.Select(t => new RestTableInfo
            {
                Name = t.Name,
                Columns = [],
            }).ToArray();

            return Ok(new SchemaResponse
            {
                Database = databaseName,
                Tables = tableInfos,
                LastUpdated = DateTimeOffset.UtcNow,
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Schema retrieval failed for '{Database}'", databaseName);
            return StatusCode(500, new ErrorResponse { Error = "Schema retrieval failed", Code = "SCHEMA_ERROR", Details = ex.Message });
        }
    }

    /// <summary>
    /// Lists all hosted databases.
    /// </summary>
    [HttpGet("databases")]
    [ProducesResponseType(typeof(DatabaseListResponse), 200)]
    public IActionResult ListDatabases()
    {
        var databases = databaseRegistry.DatabaseNames.Select(name =>
        {
            var db = databaseRegistry.GetDatabase(name);
            return new DatabaseInfo
            {
                Name = name,
                IsSystemDatabase = db?.Configuration.IsSystemDatabase ?? false,
                IsReadOnly = db?.Configuration.IsReadOnly ?? false,
            };
        }).ToArray();

        return Ok(new DatabaseListResponse { Databases = databases });
    }

    /// <summary>
    /// Authenticates a user and returns a JWT bearer token.
    /// </summary>
    [HttpPost("auth/login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponse), 200)]
    [ProducesResponseType(typeof(ErrorResponse), 401)]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Unauthorized(new ErrorResponse
            {
                Error = "Invalid credentials",
                Code = "INVALID_CREDENTIALS",
                Details = "Username and password are required",
            });
        }

        var sessionId = Guid.NewGuid().ToString();
        var result = authService.Authenticate(request.Username, request.Password, sessionId);

        if (!result.IsAuthenticated)
        {
            return Unauthorized(new ErrorResponse
            {
                Error = "Invalid credentials",
                Code = "INVALID_CREDENTIALS",
                Details = result.ErrorMessage ?? "Authentication failed",
            });
        }

        logger.LogInformation("REST login succeeded for user '{Username}' (role={Role})",
            request.Username, result.Role);

        return Ok(new LoginResponse
        {
            Token = result.Token!,
            Role = result.Role.ToString().ToLowerInvariant(),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(24),
        });
    }

    /// <summary>
    /// Gets server health and statistics. No authentication required.
    /// </summary>
    [HttpGet("health")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(HealthResponse), 200)]
    public IActionResult GetHealth()
    {
        var detailed = healthCheckService.GetDetailedHealth();

        return Ok(new HealthResponse
        {
            Status = detailed.Status,
            Timestamp = DateTimeOffset.UtcNow,
            Version = detailed.Version,
            ActiveConnections = detailed.ActiveConnections,
            ActiveSessions = detailed.ActiveSessions,
            TotalDatabases = detailed.HostedDatabases,
            DatabasesOnline = detailed.DatabasesOnline,
            ErrorRatePercent = detailed.ErrorRatePercent,
            LastFailureCode = detailed.LastFailureCode,
            Uptime = ServerUptime.Elapsed.ToString(@"d\.hh\:mm\:ss"),
            Checks = detailed.Checks,
        });
    }

    /// <summary>
    /// Gets server metrics for monitoring dashboards.
    /// </summary>
    [HttpGet("metrics")]
    [Authorize(Roles = "admin,writer,reader")]
    [ProducesResponseType(typeof(MetricsResponse), 200)]
    public IActionResult GetMetrics()
    {
        var snapshot = metricsCollector.GetSnapshot();
        var memoryMb = GC.GetTotalMemory(forceFullCollection: false) / (1024.0 * 1024.0);

        return Ok(new MetricsResponse
        {
            Timestamp = snapshot.Timestamp,
            ActiveConnections = snapshot.ActiveConnections,
            ActiveSessions = snapshot.ActiveSessions,
            TotalConnections = snapshot.Protocols.Values.Sum(static protocol => protocol.TotalConnections),
            TotalRequests = snapshot.TotalRequests,
            FailedRequests = snapshot.FailedRequests,
            ErrorRatePercent = snapshot.ErrorRatePercent,
            AverageLatencyMs = snapshot.AverageLatencyMs,
            QueriesPerSecond = snapshot.LastRequestAgeSeconds <= 1 && snapshot.TotalRequests > 0 ? 1 : 0,
            QueryRequests = snapshot.QueryRequests,
            NonQueryRequests = snapshot.NonQueryRequests,
            TotalRowsReturned = snapshot.TotalRowsReturned,
            TotalBytesReceived = snapshot.TotalBytesReceived,
            TotalBytesSent = snapshot.TotalBytesSent,
            LastFailureCode = snapshot.LastFailureCode,
            LastFailureTimestamp = snapshot.LastFailureTimestamp,
            MemoryUsageMb = Math.Round(memoryMb, 2),
            CpuUsagePercent = 0,
            ProtocolMetrics = snapshot.Protocols,
            DatabaseMetrics = databaseRegistry.DatabaseNames.ToDictionary(
                name => name,
                name => new DatabaseMetrics { Name = name, SizeMb = 0, ConnectionCount = 0 }),
        });
    }

    /// <summary>
    /// Gets recent tenant access authorization audit events.
    /// </summary>
    [HttpGet("tenant-access/audit")]
    [Authorize(Roles = "admin")]
    [ProducesResponseType(typeof(TenantAccessAuditResponse), 200)]
    public IActionResult GetTenantAccessAudit(
        [FromQuery] int maxCount = 100,
        [FromQuery] bool deniedOnly = false)
    {
        var events = tenantAccessAuditStore.GetRecent(maxCount, deniedOnly);

        return Ok(new TenantAccessAuditResponse
        {
            TotalRetained = tenantAccessAuditStore.Count,
            Returned = events.Count,
            DeniedOnly = deniedOnly,
            Events = events.Select(static e => new TenantAccessAuditItem
            {
                TimestampUtc = e.TimestampUtc,
                IsAllowed = e.IsAllowed,
                Code = e.Code,
                Reason = e.Reason,
                Username = e.Username,
                TenantId = e.TenantId,
                Database = e.DatabaseName,
                Protocol = e.Protocol,
                Operation = e.Operation,
            }).ToArray(),
        });
    }

    /// <summary>
    /// Gets recent tenant security audit events.
    /// </summary>
    [HttpGet("tenant-security/audit")]
    [Authorize(Roles = "admin")]
    [ProducesResponseType(typeof(TenantSecurityAuditResponse), 200)]
    public IActionResult GetTenantSecurityAudit([FromQuery] int maxCount = 100)
    {
        var events = tenantSecurityAuditStore.GetRecent(maxCount);

        return Ok(new TenantSecurityAuditResponse
        {
            TotalRetained = tenantSecurityAuditStore.Count,
            Returned = events.Count,
            Events = events.Select(static e => new TenantSecurityAuditItem
            {
                TimestampUtc = e.TimestampUtc,
                EventType = e.EventType.ToString(),
                TenantId = e.TenantId,
                Database = e.DatabaseName,
                Principal = e.Principal,
                Protocol = e.Protocol,
                IsAllowed = e.IsAllowed,
                DecisionCode = e.DecisionCode,
                Reason = e.Reason,
            }).ToArray(),
        });
    }

    /// <summary>
    /// Resolves a database and creates a short-lived session for the REST request.
    /// </summary>
    private async Task<(DatabaseInstance? db, ClientSession? session, IActionResult? error)>
        ResolveSessionAsync(
        string? databaseName,
        DatabasePermission requiredPermission,
        CancellationToken cancellationToken)
    {
        var name = databaseName ?? "master";
        var db = databaseRegistry.GetDatabase(name);
        if (db is null)
        {
            var err = BadRequest(new ErrorResponse { Error = "Database not found", Code = "DATABASE_NOT_FOUND", Details = $"Database '{name}' does not exist" });
            return (null, null, err);
        }

        var decision = tenantAuthorizationPolicyService.AuthorizeDatabaseAccess(
            User,
            name,
            requiredPermission,
            protocol: "REST",
            operation: HttpContext.Request.Path);

        if (!decision.IsAllowed)
        {
            var err = Forbid();
            return (null, null, err);
        }

        var role = RbacService.GetRoleFromPrincipal(User);
        var tenantId = User.GetTenantId() ?? authService.GetUserTenantId(User.Identity?.Name ?? string.Empty) ?? "default";

        var session = await sessionManager.CreateSessionAsync(
            name,
            User.Identity?.Name ?? "anonymous",
            HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            role,
            tenantId,
            cancellationToken);

        return (db, session, null);
    }
}

// ── REST API Request/Response DTOs ──

/// <summary>Query request parameters.</summary>
public sealed class QueryRequest
{
    /// <summary>SQL query text.</summary>
    public required string Sql { get; init; }

    /// <summary>Query parameters.</summary>
    public Dictionary<string, object?>? Parameters { get; init; }

    /// <summary>Target database name (defaults to master).</summary>
    public string? Database { get; init; }

    /// <summary>Query timeout in milliseconds.</summary>
    public int? TimeoutMs { get; init; }
}

/// <summary>Query response data.</summary>
public sealed class QueryResponse
{
    /// <summary>Column information.</summary>
    public required ColumnInfo[] Columns { get; init; }

    /// <summary>Result rows.</summary>
    public required object?[][] Rows { get; init; }

    /// <summary>Number of rows returned.</summary>
    public required int RowsAffected { get; init; }

    /// <summary>Execution time in milliseconds.</summary>
    public required double ExecutionTimeMs { get; init; }
}

/// <summary>Column information.</summary>
public sealed class ColumnInfo
{
    /// <summary>Column name.</summary>
    public required string Name { get; init; }

    /// <summary>Column data type.</summary>
    public required string Type { get; init; }

    /// <summary>Whether column is nullable.</summary>
    public required bool Nullable { get; init; }
}

/// <summary>Execute request parameters.</summary>
public sealed class ExecuteRequest
{
    /// <summary>SQL statement text.</summary>
    public required string Sql { get; init; }

    /// <summary>Statement parameters.</summary>
    public Dictionary<string, object?>? Parameters { get; init; }

    /// <summary>Target database name.</summary>
    public string? Database { get; init; }

    /// <summary>Execution timeout in milliseconds.</summary>
    public int? TimeoutMs { get; init; }
}

/// <summary>Execute response data.</summary>
public sealed class ExecuteResponse
{
    /// <summary>Number of rows affected.</summary>
    public required int RowsAffected { get; init; }

    /// <summary>Execution time in milliseconds.</summary>
    public required double ExecutionTimeMs { get; init; }
}

/// <summary>Batch request parameters.</summary>
public sealed class BatchRequest
{
    /// <summary>SQL statements to execute.</summary>
    public required string[] Statements { get; init; }

    /// <summary>Target database name.</summary>
    public string? Database { get; init; }

    /// <summary>Whether to execute in a transaction.</summary>
    public bool Transactional { get; init; }
}

/// <summary>Batch response data.</summary>
public sealed class BatchResponse
{
    /// <summary>Number of statements executed.</summary>
    public required int StatementsExecuted { get; init; }

    /// <summary>Total execution time in milliseconds.</summary>
    public required double TotalExecutionTimeMs { get; init; }

    /// <summary>Whether all statements succeeded.</summary>
    public required bool Success { get; init; }
}

/// <summary>Schema response data.</summary>
public sealed class SchemaResponse
{
    /// <summary>Database name.</summary>
    public required string Database { get; init; }

    /// <summary>Tables in the database.</summary>
    public required RestTableInfo[] Tables { get; init; }

    /// <summary>Last schema update timestamp.</summary>
    public required DateTimeOffset LastUpdated { get; init; }
}

/// <summary>Table information.</summary>
public sealed class RestTableInfo
{
    /// <summary>Table name.</summary>
    public required string Name { get; init; }

    /// <summary>Table columns.</summary>
    public required ColumnInfo[] Columns { get; init; }

    /// <summary>Number of rows in table.</summary>
    public long? RowCount { get; init; }
}

/// <summary>Database list response.</summary>
public sealed class DatabaseListResponse
{
    /// <summary>All hosted databases.</summary>
    public required DatabaseInfo[] Databases { get; init; }
}

/// <summary>Database information.</summary>
public sealed class DatabaseInfo
{
    /// <summary>Database name.</summary>
    public required string Name { get; init; }

    /// <summary>Whether this is a system database.</summary>
    public required bool IsSystemDatabase { get; init; }

    /// <summary>Whether this database is read-only.</summary>
    public required bool IsReadOnly { get; init; }
}

/// <summary>Health response data.</summary>
public sealed class HealthResponse
{
    /// <summary>Health status.</summary>
    public required string Status { get; init; }

    /// <summary>Response timestamp.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Server version.</summary>
    public required string Version { get; init; }

    /// <summary>Active connections.</summary>
    public required int ActiveConnections { get; init; }

    /// <summary>Active sessions.</summary>
    public required int ActiveSessions { get; init; }

    /// <summary>Total databases.</summary>
    public required int TotalDatabases { get; init; }

    /// <summary>Databases currently online.</summary>
    public required int DatabasesOnline { get; init; }

    /// <summary>Current failed request percentage.</summary>
    public required double ErrorRatePercent { get; init; }

    /// <summary>Most recent error code observed in metrics.</summary>
    public required string LastFailureCode { get; init; }

    /// <summary>Server uptime (d.hh:mm:ss).</summary>
    public required string Uptime { get; init; }

    /// <summary>Named health checks for quick triage.</summary>
    public required Dictionary<string, string> Checks { get; init; }
}

/// <summary>Metrics response data.</summary>
public sealed class MetricsResponse
{
    /// <summary>Response timestamp.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Queries per second.</summary>
    public required double QueriesPerSecond { get; init; }

    /// <summary>Active connections.</summary>
    public required int ActiveConnections { get; init; }

    /// <summary>Active sessions.</summary>
    public required int ActiveSessions { get; init; }

    /// <summary>Total connections since startup.</summary>
    public required long TotalConnections { get; init; }

    /// <summary>Total requests since startup.</summary>
    public required long TotalRequests { get; init; }

    /// <summary>Total failed requests since startup.</summary>
    public required long FailedRequests { get; init; }

    /// <summary>Current failed request percentage.</summary>
    public required double ErrorRatePercent { get; init; }

    /// <summary>Average request latency in milliseconds.</summary>
    public required double AverageLatencyMs { get; init; }

    /// <summary>Total query-style requests.</summary>
    public required long QueryRequests { get; init; }

    /// <summary>Total non-query requests.</summary>
    public required long NonQueryRequests { get; init; }

    /// <summary>Total rows returned by query operations.</summary>
    public required long TotalRowsReturned { get; init; }

    /// <summary>Total network payload bytes received.</summary>
    public required long TotalBytesReceived { get; init; }

    /// <summary>Total network payload bytes sent.</summary>
    public required long TotalBytesSent { get; init; }

    /// <summary>Most recent failure code.</summary>
    public required string LastFailureCode { get; init; }

    /// <summary>Timestamp of the most recent failure.</summary>
    public required DateTimeOffset? LastFailureTimestamp { get; init; }

    /// <summary>Memory usage in MB.</summary>
    public required double MemoryUsageMb { get; init; }

    /// <summary>CPU usage percentage.</summary>
    public required double CpuUsagePercent { get; init; }

    /// <summary>Per-protocol counters for requests, errors, and messages.</summary>
    public required Dictionary<string, ProtocolMetricsSnapshot> ProtocolMetrics { get; init; }

    /// <summary>Per-database metrics.</summary>
    public required Dictionary<string, DatabaseMetrics> DatabaseMetrics { get; init; }
}

/// <summary>Database metrics.</summary>
public sealed class DatabaseMetrics
{
    /// <summary>Database name.</summary>
    public required string Name { get; init; }

    /// <summary>Database size in MB.</summary>
    public required double SizeMb { get; init; }

    /// <summary>Active connections to this database.</summary>
    public required int ConnectionCount { get; init; }
}

/// <summary>Error response data.</summary>
public sealed class ErrorResponse
{
    /// <summary>Error message.</summary>
    public required string Error { get; init; }

    /// <summary>Error code.</summary>
    public required string Code { get; init; }

    /// <summary>Additional error details.</summary>
    public string? Details { get; init; }
}

/// <summary>Login request parameters.</summary>
public sealed class LoginRequest
{
    /// <summary>Username.</summary>
    public required string Username { get; init; }

    /// <summary>Plaintext password.</summary>
    public required string Password { get; init; }
}

/// <summary>Login response with JWT token.</summary>
public sealed class LoginResponse
{
    /// <summary>JWT bearer token.</summary>
    public required string Token { get; init; }

    /// <summary>Assigned role (admin, writer, reader).</summary>
    public required string Role { get; init; }

    /// <summary>Token expiration timestamp.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>Tenant access audit response payload.</summary>
public sealed class TenantAccessAuditResponse
{
    /// <summary>Total retained audit events in memory.</summary>
    public required int TotalRetained { get; init; }

    /// <summary>Number of returned events.</summary>
    public required int Returned { get; init; }

    /// <summary>Whether response is denied-only filtered.</summary>
    public required bool DeniedOnly { get; init; }

    /// <summary>Returned audit events.</summary>
    public required TenantAccessAuditItem[] Events { get; init; }
}

/// <summary>Single tenant access audit item.</summary>
public sealed class TenantAccessAuditItem
{
    /// <summary>Event timestamp in UTC.</summary>
    public required DateTime TimestampUtc { get; init; }

    /// <summary>Authorization decision.</summary>
    public required bool IsAllowed { get; init; }

    /// <summary>Decision code.</summary>
    public required string Code { get; init; }

    /// <summary>Decision reason.</summary>
    public required string Reason { get; init; }

    /// <summary>User name for the request.</summary>
    public required string Username { get; init; }

    /// <summary>Tenant identifier.</summary>
    public required string TenantId { get; init; }

    /// <summary>Database name.</summary>
    public required string Database { get; init; }

    /// <summary>Protocol name.</summary>
    public required string Protocol { get; init; }

    /// <summary>Operation identifier.</summary>
    public required string Operation { get; init; }
}

/// <summary>Tenant security audit response payload.</summary>
public sealed class TenantSecurityAuditResponse
{
    /// <summary>Total retained audit events in memory.</summary>
    public required int TotalRetained { get; init; }

    /// <summary>Number of returned events.</summary>
    public required int Returned { get; init; }

    /// <summary>Returned security audit events.</summary>
    public required TenantSecurityAuditItem[] Events { get; init; }
}

/// <summary>Single tenant security audit item.</summary>
public sealed class TenantSecurityAuditItem
{
    /// <summary>Event timestamp in UTC.</summary>
    public required DateTime TimestampUtc { get; init; }

    /// <summary>Security event type.</summary>
    public required string EventType { get; init; }

    /// <summary>Tenant identifier.</summary>
    public required string TenantId { get; init; }

    /// <summary>Database name.</summary>
    public required string Database { get; init; }

    /// <summary>Principal identifier.</summary>
    public required string Principal { get; init; }

    /// <summary>Protocol.</summary>
    public required string Protocol { get; init; }

    /// <summary>Authorization decision.</summary>
    public required bool IsAllowed { get; init; }

    /// <summary>Decision code.</summary>
    public required string DecisionCode { get; init; }

    /// <summary>Decision reason.</summary>
    public required string Reason { get; init; }
}
