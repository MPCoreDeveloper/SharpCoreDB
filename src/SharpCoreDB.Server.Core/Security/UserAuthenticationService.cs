// <copyright file="UserAuthenticationService.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpCoreDB.Server.Core.Observability;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace SharpCoreDB.Server.Core.Security;

public sealed class UserAuthenticationService(
    IOptions<ServerConfiguration> configuration,
    JwtTokenService tokenService,
    TenantSecurityAuditService securityAuditService,
    ILogger<UserAuthenticationService> logger)
{
    private readonly ServerConfiguration _config = configuration.Value;
    private readonly JwtTokenService _tokenService = tokenService;
    private readonly TenantSecurityAuditService _securityAuditService = securityAuditService;
    private readonly ILogger<UserAuthenticationService> _logger = logger;

    /// <summary>
    /// Authenticates a user and returns a result with JWT token and role.
    /// </summary>
    /// <param name="username">Login username.</param>
    /// <param name="password">Plaintext password.</param>
    /// <param name="sessionId">Session identifier to embed in the token.</param>
    /// <returns>Authentication result.</returns>
    public AuthenticationResult Authenticate(string username, string password, string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var user = _config.Security.Users
            .FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

        if (user is null)
        {
            _logger.LogWarning("Authentication failed: user '{Username}' not found", username);
            EmitLoginEvent(
                eventType: TenantSecurityEventType.LoginFailed,
                tenantId: "unknown",
                principal: username,
                isAllowed: false,
                code: "LOGIN_USER_NOT_FOUND",
                reason: "User not found");

            return AuthenticationResult.Failed("Invalid username or password");
        }

        var passwordHash = ComputeSha256Hash(password);
        if (!string.Equals(passwordHash, user.PasswordHash, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Authentication failed: invalid password for user '{Username}'", username);
            EmitLoginEvent(
                eventType: TenantSecurityEventType.LoginFailed,
                tenantId: string.IsNullOrWhiteSpace(user.TenantId) ? "default" : user.TenantId,
                principal: username,
                isAllowed: false,
                code: "LOGIN_INVALID_PASSWORD",
                reason: "Password hash mismatch");

            return AuthenticationResult.Failed("Invalid username or password");
        }

        var role = ParseRole(user.Role);
        var tenantScope = BuildTenantScope(user, role);
        var token = _tokenService.GenerateToken(username, sessionId, user.Role, tenantScope);

        _logger.LogInformation(
            "User '{Username}' authenticated successfully with role '{Role}' and tenant '{TenantId}' scope",
            username, role, tenantScope.TenantId);

        EmitLoginEvent(
            eventType: TenantSecurityEventType.LoginSucceeded,
            tenantId: tenantScope.TenantId,
            principal: username,
            isAllowed: true,
            code: "LOGIN_OK",
            reason: "Authentication succeeded");

        return AuthenticationResult.Succeeded(token, role);
    }

    /// <summary>
    /// Creates a tenant-aware claims principal for a configured user.
    /// Intended for non-JWT protocol paths that still require equivalent policy evaluation.
    /// </summary>
    /// <param name="username">Configured username.</param>
    /// <returns>Claims principal for the configured user or <see langword="null"/> if user does not exist.</returns>
    public ClaimsPrincipal? CreatePrincipalForUser(string username)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        var user = _config.Security.Users
            .FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

        if (user is null)
        {
            return null;
        }

        var role = ParseRole(user.Role);
        var tenantScope = BuildTenantScope(user, role);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Username),
            new(ClaimTypes.Role, user.Role),
        };

        claims.AddRange(TenantAwareTokenService
            .CreateClaimsBuilder()
            .WithTenantId(tenantScope.TenantId)
            .WithAllowedDatabases([..tenantScope.AllowedDatabases])
            .WithDatabasePermissions(tenantScope.Permissions)
            .WithScopeVersion(tenantScope.ScopeVersion)
            .Build());

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "ConfigurationUser"));
    }

    /// <summary>
    /// Validates that a user exists and returns their configured role.
    /// Does not verify password — use for session lookups where JWT is already validated.
    /// </summary>
    /// <param name="username">Login username.</param>
    /// <returns>The user's role, or null if user not found.</returns>
    public DatabaseRole? GetUserRole(string username)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        var user = _config.Security.Users
            .FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

        return user is null ? null : ParseRole(user.Role);
    }

    /// <summary>
    /// Gets the configured tenant identifier for a user.
    /// </summary>
    /// <param name="username">Login username.</param>
    /// <returns>Tenant identifier or <see langword="null"/> when user is not found.</returns>
    public string? GetUserTenantId(string username)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        var user = _config.Security.Users
            .FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

        if (user is null)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(user.TenantId)
            ? "default"
            : user.TenantId.Trim();
    }

    private static JwtTenantScope BuildTenantScope(UserConfiguration user, DatabaseRole role)
    {
        ArgumentNullException.ThrowIfNull(user);

        var tenantId = string.IsNullOrWhiteSpace(user.TenantId)
            ? "default"
            : user.TenantId.Trim();

        var allowedDatabases = user.AllowedDatabases
            .Where(static db => !string.IsNullOrWhiteSpace(db))
            .Select(static db => db.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (allowedDatabases.Length == 0)
        {
            allowedDatabases = ["*"];
        }

        var scopeVersion = string.IsNullOrWhiteSpace(user.ScopeVersion)
            ? TenantAwareClaims.CurrentScopeVersion
            : user.ScopeVersion.Trim();

        return new JwtTenantScope(tenantId, allowedDatabases, MapRoleToDatabasePermission(role), scopeVersion);
    }

    private static DatabasePermission MapRoleToDatabasePermission(DatabaseRole role) => role switch
    {
        DatabaseRole.Admin => DatabasePermission.All,
        DatabaseRole.Writer => DatabasePermission.Connect
            | DatabasePermission.Select
            | DatabasePermission.Insert
            | DatabasePermission.Update
            | DatabasePermission.Delete
            | DatabasePermission.Execute,
        _ => DatabasePermission.Connect | DatabasePermission.Select,
    };

    private static DatabaseRole ParseRole(string role) => role.ToLowerInvariant() switch
    {
        "admin" => DatabaseRole.Admin,
        "writer" => DatabaseRole.Writer,
        "reader" => DatabaseRole.Reader,
        _ => DatabaseRole.Reader,
    };

    private static string ComputeSha256Hash(string input)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(hashBytes);
    }

    private void EmitLoginEvent(
        TenantSecurityEventType eventType,
        string tenantId,
        string principal,
        bool isAllowed,
        string code,
        string reason)
    {
        _securityAuditService.Emit(new TenantSecurityAuditEvent(
            TimestampUtc: DateTime.UtcNow,
            EventType: eventType,
            TenantId: string.IsNullOrWhiteSpace(tenantId) ? "default" : tenantId,
            DatabaseName: "n/a",
            Principal: principal,
            Protocol: "Auth",
            IsAllowed: isAllowed,
            DecisionCode: code,
            Reason: reason));
    }
}

/// <summary>
/// Result of a user authentication attempt.
/// </summary>
public sealed class AuthenticationResult
{
    /// <summary>Whether authentication succeeded.</summary>
    public bool IsAuthenticated { get; private init; }

    /// <summary>JWT token (only set on success).</summary>
    public string? Token { get; private init; }

    /// <summary>Authenticated user's role (only set on success).</summary>
    public DatabaseRole Role { get; private init; }

    /// <summary>Error message (only set on failure).</summary>
    public string? ErrorMessage { get; private init; }

    /// <summary>Creates a successful authentication result.</summary>
    public static AuthenticationResult Succeeded(string token, DatabaseRole role) => new()
    {
        IsAuthenticated = true,
        Token = token,
        Role = role,
    };

    /// <summary>Creates a failed authentication result.</summary>
    public static AuthenticationResult Failed(string errorMessage) => new()
    {
        IsAuthenticated = false,
        ErrorMessage = errorMessage,
    };
}
