// <copyright file="SessionManager.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.Extensions.Logging;
using SharpCoreDB.Server.Core.Observability;
using SharpCoreDB.Server.Core.Security;
using SharpCoreDB.Server.Core.Tenancy;
using System.Collections.Concurrent;

namespace SharpCoreDB.Server.Core;

/// <summary>
/// Manages client sessions and authentication.
/// Tracks active connections and enforces security policies.
/// C# 14: Uses primary constructor and collection expressions.
/// </summary>
public sealed class SessionManager(
    DatabaseRegistry databaseRegistry,
    TenantQuotaEnforcementService tenantQuotaEnforcementService,
    ILogger<SessionManager> logger,
    DatabaseGrantsRepository? databaseGrantsRepository = null,
    DatabaseAuthorizationService? databaseAuthorizationService = null,
    MetricsCollector? metricsCollector = null)
{
    private readonly DatabaseRegistry _databaseRegistry = databaseRegistry;
    private readonly TenantQuotaEnforcementService _tenantQuotaEnforcementService = tenantQuotaEnforcementService;
    private readonly DatabaseGrantsRepository? _databaseGrantsRepository = databaseGrantsRepository;
    private readonly DatabaseAuthorizationService? _databaseAuthorizationService = databaseAuthorizationService;
    private readonly MetricsCollector? _metricsCollector = metricsCollector;
    private readonly ILogger<SessionManager> _logger = logger;
    private readonly ConcurrentDictionary<string, ClientSession> _sessions = new();
    private readonly Lock _sessionLock = new();

    /// <summary>
    /// Gets the number of active sessions.
    /// </summary>
    public int ActiveSessionCount => _sessions.Count;

    /// <summary>
    /// Creates a new client session.
    /// </summary>
    /// <param name="databaseName">Target database name.</param>
    /// <param name="userName">Client username.</param>
    /// <param name="clientAddress">Client network address.</param>
    /// <param name="role">Authenticated user role.</param>
    /// <param name="tenantId">Tenant identifier for quota evaluation.</param>
    /// <param name="principal">Validated JWT claims principal for scope enforcement (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>New client session.</returns>
    public async Task<ClientSession> CreateSessionAsync(
        string databaseName,
        string? userName,
        string clientAddress,
        DatabaseRole role = DatabaseRole.Reader,
        string tenantId = "default",
        System.Security.Claims.ClaimsPrincipal? principal = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var database = _databaseRegistry.GetDatabase(databaseName);
        if (database == null)
        {
            throw new ArgumentException($"Database '{databaseName}' not found", nameof(databaseName));
        }

        // Enforce JWT claims scope if principal is provided
        if (principal is not null && role != DatabaseRole.Admin)
        {
            var scopeVersion = principal.GetScopeVersion();
            if (!string.IsNullOrWhiteSpace(scopeVersion))
            {
                if (!principal.HasDatabaseAccess(databaseName))
                {
                    _metricsCollector?.RecordFailedRequest("session/create", "JWT_SCOPE_DENIED");
                    _logger.LogWarning(
                        "Session creation denied by JWT scope for user '{User}' tenant '{TenantId}' database '{Database}' (scope_version={ScopeVersion})",
                        userName,
                        tenantId,
                        databaseName,
                        scopeVersion);

                    throw new UnauthorizedAccessException("JWT_SCOPE_DENIED: database is not in token's allowed_databases claim.");
                }

                var requiredPermission = DatabasePermission.Connect;
                if (!principal.HasDatabasePermission(requiredPermission))
                {
                    _metricsCollector?.RecordFailedRequest("session/create", "JWT_PERMISSION_DENIED");
                    _logger.LogWarning(
                        "Session creation denied by JWT permission for user '{User}' tenant '{TenantId}' database '{Database}' (required={Required})",
                        userName,
                        tenantId,
                        databaseName,
                        requiredPermission);

                    throw new UnauthorizedAccessException("JWT_PERMISSION_DENIED: token does not have CONNECT permission.");
                }
            }
        }

        if (role != DatabaseRole.Admin &&
            !string.IsNullOrWhiteSpace(userName) &&
            _databaseGrantsRepository is not null &&
            _databaseAuthorizationService is not null)
        {
            var activeGrants = await _databaseGrantsRepository
                .GetPrincipalGrantsAsync(userName, cancellationToken)
                .ConfigureAwait(false);

            if (activeGrants.Count > 0)
            {
                var isAllowed = await _databaseAuthorizationService
                    .AuthorizeOperationAsync(
                        tenantId,
                        databaseName,
                        userName,
                        DatabasePermission.Connect,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (!isAllowed)
                {
                    _metricsCollector?.RecordFailedRequest("session/create", "DATABASE_GRANT_DENIED");
                    _logger.LogWarning(
                        "Session creation denied by grants for user '{User}' tenant '{TenantId}' database '{Database}'",
                        userName,
                        tenantId,
                        databaseName);

                    throw new UnauthorizedAccessException("DATABASE_GRANT_DENIED: principal is not granted CONNECT on the requested database.");
                }
            }
        }

        var tenantSessionCount = _sessions.Values.Count(s => string.Equals(s.TenantId, tenantId, StringComparison.Ordinal));
        await _tenantQuotaEnforcementService
            .EnsureSessionQuotaAsync(tenantId, tenantSessionCount, cancellationToken)
            .ConfigureAwait(false);

        var sessionId = Guid.NewGuid().ToString();
        var session = new ClientSession(sessionId, database, userName, clientAddress, DateTimeOffset.UtcNow, role, tenantId);

        if (!_sessions.TryAdd(sessionId, session))
        {
            throw new InvalidOperationException($"Session ID collision: {sessionId}");
        }

        _logger.LogInformation(
            "Created session {SessionId} for user '{User}' tenant '{TenantId}' (role={Role}) from {ClientAddress} to database '{Database}'",
            sessionId,
            userName ?? "anonymous",
            tenantId,
            role,
            clientAddress,
            databaseName);

        _metricsCollector?.RecordSuccessfulRequest("session/create", 0, 0, 0, 0);

        return session;
    }

    /// <summary>
    /// Gets an existing session by ID.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Client session or null if not found.</returns>
    public Task<ClientSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        if (_sessions.TryGetValue(sessionId, out var session))
        {
            // Update last activity
            session.LastActivity = DateTimeOffset.UtcNow;
            return Task.FromResult<ClientSession?>(session);
        }

        return Task.FromResult<ClientSession?>(null);
    }

    /// <summary>
    /// Removes a session.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task RemoveSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        if (_sessions.TryRemove(sessionId, out var session))
        {
            _logger.LogInformation("Removed session {SessionId} for user '{User}'",
                sessionId, session.UserName ?? "anonymous");

            // Cleanup session resources
            session.ActiveTransactionId = null;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Validates session authentication and RBAC permissions.
    /// </summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <param name="requiredPermission">Required permission.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if session is valid and has the required permission.</returns>
    public async Task<bool> ValidateSessionAsync(
        string sessionId,
        Permission requiredPermission = Permission.Read,
        CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken);
        if (session == null)
        {
            return false;
        }

        // Check session timeout (30 minutes default)
        if (DateTimeOffset.UtcNow - session.LastActivity > TimeSpan.FromMinutes(30))
        {
            await RemoveSessionAsync(sessionId, cancellationToken);
            return false;
        }

        return RbacService.HasPermission(session.Role, requiredPermission);
    }

    /// <summary>
    /// Gets all active sessions for monitoring.
    /// </summary>
    /// <returns>Collection of active sessions.</returns>
    public IReadOnlyCollection<ClientSession> GetActiveSessions()
    {
        return _sessions.Values.ToArray();
    }

    /// <summary>
    /// Cleans up expired sessions.
    /// </summary>
    /// <param name="maxAge">Maximum session age.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task CleanupExpiredSessionsAsync(
        TimeSpan maxAge = default,
        CancellationToken cancellationToken = default)
    {
        if (maxAge == default)
        {
            maxAge = TimeSpan.FromMinutes(30);
        }

        var expiredSessions = new List<string>();
        var now = DateTimeOffset.UtcNow;

        foreach (var (sessionId, session) in _sessions)
        {
            if (now - session.LastActivity > maxAge)
            {
                expiredSessions.Add(sessionId);
            }
        }

        foreach (var sessionId in expiredSessions)
        {
            await RemoveSessionAsync(sessionId, cancellationToken);
        }

        if (expiredSessions.Count > 0)
        {
            _logger.LogInformation("Cleaned up {Count} expired sessions", expiredSessions.Count);
        }
    }
}

/// <summary>
/// Represents a client database session.
/// Tracks connection state, authentication, and active transactions.
/// C# 14: Uses primary constructor for immutability.
/// </summary>
public sealed class ClientSession(
    string sessionId,
    DatabaseInstance databaseInstance,
    string? userName,
    string clientAddress,
    DateTimeOffset createdAt,
    DatabaseRole role = DatabaseRole.Reader,
    string tenantId = "default")
{
    /// <summary>
    /// Gets the unique session identifier.
    /// </summary>
    public string SessionId { get; } = sessionId;

    /// <summary>
    /// Gets the target database instance.
    /// </summary>
    public DatabaseInstance DatabaseInstance { get; } = databaseInstance;

    /// <summary>
    /// Gets the authenticated username.
    /// </summary>
    public string? UserName { get; } = userName;

    /// <summary>
    /// Gets the owning tenant identifier.
    /// </summary>
    public string TenantId { get; } = tenantId;

    /// <summary>
    /// Gets the client network address.
    /// </summary>
    public string ClientAddress { get; } = clientAddress;

    /// <summary>
    /// Gets the session creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; } = createdAt;

    /// <summary>
    /// Gets the authenticated user's RBAC role.
    /// </summary>
    public DatabaseRole Role { get; } = role;

    /// <summary>
    /// Gets or sets the last activity timestamp.
    /// </summary>
    public DateTimeOffset LastActivity { get; set; } = createdAt;

    /// <summary>
    /// Gets or sets the active transaction ID.
    /// </summary>
    public string? ActiveTransactionId { get; set; }

    /// <summary>
    /// Gets whether the session has an active transaction.
    /// </summary>
    public bool HasActiveTransaction => !string.IsNullOrEmpty(ActiveTransactionId);

    /// <summary>
    /// Gets the session duration.
    /// </summary>
    public TimeSpan Duration => DateTimeOffset.UtcNow - CreatedAt;
}
