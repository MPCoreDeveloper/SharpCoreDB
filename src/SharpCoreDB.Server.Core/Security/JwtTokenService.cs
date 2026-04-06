// <copyright file="JwtTokenService.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace SharpCoreDB.Server.Core.Security;

/// <summary>
/// Produces and validates JWT bearer tokens for gRPC clients.
/// C# 14: Primary constructor with immutable fields.
/// </summary>
public sealed class JwtTokenService(
    string secretKey,
    int expirationHours = 24,
    bool allowLegacyTokens = true)
{
    private readonly string _secretKey = secretKey ?? throw new ArgumentNullException(nameof(secretKey));
    private readonly int _expirationHours = expirationHours;
    private readonly bool _allowLegacyTokens = allowLegacyTokens;
    private readonly JwtSecurityTokenHandler _tokenHandler = new();

    /// <summary>
    /// Generates a new JWT token for the specified username and session.
    /// </summary>
    public string GenerateToken(
        string username,
        string sessionId,
        string? roles = null,
        JwtTenantScope? tenantScope = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var key = Encoding.ASCII.GetBytes(_secretKey);
        var signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(key),
            SecurityAlgorithms.HmacSha256Signature);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, username),
            new("session_id", sessionId),
            new("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()),
        };

        if (!string.IsNullOrWhiteSpace(roles))
        {
            foreach (var role in roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }
        }

        if (tenantScope is not null)
        {
            claims.AddRange(BuildTenantScopeClaims(tenantScope));
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(_expirationHours),
            SigningCredentials = signingCredentials,
            Audience = "sharpcoredb-server",
            Issuer = "sharpcoredb-server",
        };

        var token = _tokenHandler.CreateToken(tokenDescriptor);
        return _tokenHandler.WriteToken(token);
    }

    /// <summary>
    /// Validates and extracts claims from a JWT token.
    /// </summary>
    public ClaimsPrincipal ValidateToken(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        try
        {
            var key = Encoding.ASCII.GetBytes(_secretKey);
            var tokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = "sharpcoredb-server",
                ValidateAudience = true,
                ValidAudience = "sharpcoredb-server",
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
            };

            var principal = _tokenHandler.ValidateToken(token, tokenValidationParameters, out var validatedToken);

            if (validatedToken is not JwtSecurityToken jwtToken ||
                !jwtToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new SecurityTokenInvalidSignatureException("Token signature validation failed");
            }

            ValidateTenantScopeClaims(principal);
            return principal;
        }
        catch (SecurityTokenException ex)
        {
            throw new SecurityTokenValidationException($"Token validation failed: {ex.Message}", ex);
        }
        catch (ArgumentException ex)
        {
            throw new SecurityTokenValidationException($"Token validation failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Extracts the session ID from a JWT token.
    /// </summary>
    public string? GetSessionIdFromToken(ClaimsPrincipal principal)
    {
        return principal?.FindFirst("session_id")?.Value;
    }

    /// <summary>
    /// Extracts the username from a JWT token.
    /// </summary>
    public string? GetUsernameFromToken(ClaimsPrincipal principal)
    {
        return principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }

    private static IReadOnlyList<Claim> BuildTenantScopeClaims(JwtTenantScope tenantScope)
    {
        var builder = TenantAwareTokenService
            .CreateClaimsBuilder()
            .WithTenantId(tenantScope.TenantId)
            .WithAllowedDatabases([..tenantScope.AllowedDatabases])
            .WithDatabasePermissions(tenantScope.Permissions)
            .WithScopeVersion(tenantScope.ScopeVersion);

        return [.. builder.Build()];
    }

    private void ValidateTenantScopeClaims(ClaimsPrincipal principal)
    {
        var scopeVersion = principal.GetScopeVersion();
        var tenantId = principal.GetTenantId();
        var allowedDatabases = principal.GetAllowedDatabases();

        var hasTenantScopeData = !string.IsNullOrWhiteSpace(tenantId) || allowedDatabases.Count > 0;

        if (string.IsNullOrWhiteSpace(scopeVersion))
        {
            if (hasTenantScopeData)
            {
                throw new SecurityTokenValidationException("Token scope claims are incomplete: missing scope_version");
            }

            if (!_allowLegacyTokens)
            {
                throw new SecurityTokenValidationException("Legacy JWT tokens without scope_version are not allowed");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new SecurityTokenValidationException("Token scope claims are incomplete: missing tenant_id");
        }

        if (allowedDatabases.Count == 0)
        {
            throw new SecurityTokenValidationException("Token scope claims are incomplete: missing allowed_databases");
        }
    }
}
/// <summary>
/// Tenant scope contract embedded in JWT claims.
/// </summary>
public sealed record JwtTenantScope(
    string TenantId,
    IReadOnlyList<string> AllowedDatabases,
    DatabasePermission Permissions,
    string ScopeVersion = TenantAwareClaims.CurrentScopeVersion)
{
    /// <summary>
    /// Creates a normalized tenant scope object for JWT claim generation.
    /// </summary>
    public static JwtTenantScope Create(
        string tenantId,
        DatabasePermission permissions,
        params string[] allowedDatabases)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var normalizedDatabases = allowedDatabases
            .Where(static db => !string.IsNullOrWhiteSpace(db))
            .Select(static db => db.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedDatabases.Length == 0)
        {
            throw new ArgumentException("At least one allowed database is required", nameof(allowedDatabases));
        }

        return new JwtTenantScope(tenantId, normalizedDatabases, permissions);
    }
}
