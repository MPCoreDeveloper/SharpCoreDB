// <copyright file="GrpcAuthorizationInterceptor.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using SharpCoreDB.Server.Core.Security;

namespace SharpCoreDB.Server.Core.Grpc;

/// <summary>
/// gRPC interceptor for JWT token validation, certificate auth, RBAC, and tenant-aware authorization.
/// Supports both Bearer JWT tokens and client certificates (mutual TLS).
/// C# 14: Primary constructor with immutable dependencies.
/// </summary>
public sealed class GrpcAuthorizationInterceptor(
    JwtTokenService tokenService,
    RbacService rbacService,
    CertificateAuthenticationService certAuthService,
    TenantAuthorizationPolicyService tenantAuthorizationPolicyService,
    SessionManager sessionManager,
    IHttpContextAccessor httpContextAccessor,
    ILogger<GrpcAuthorizationInterceptor> logger) : Interceptor
{
    private readonly JwtTokenService _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
    private readonly RbacService _rbacService = rbacService ?? throw new ArgumentNullException(nameof(rbacService));
    private readonly CertificateAuthenticationService _certAuthService = certAuthService ?? throw new ArgumentNullException(nameof(certAuthService));
    private readonly TenantAuthorizationPolicyService _tenantAuthorizationPolicyService = tenantAuthorizationPolicyService ?? throw new ArgumentNullException(nameof(tenantAuthorizationPolicyService));
    private readonly SessionManager _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    private readonly ILogger<GrpcAuthorizationInterceptor> _logger = logger;

    private static readonly HashSet<string> PublicMethods =
    [
        "sharpcoredb.DatabaseService/HealthCheck",
        "sharpcoredb.DatabaseService/Connect",
    ];

    /// <inheritdoc />
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        await ValidateTokenAsync(request, context).ConfigureAwait(false);
        return await continuation(request, context).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        await ValidateTokenAsync(request, context).ConfigureAwait(false);
        await continuation(request, responseStream, context).ConfigureAwait(false);
    }

    private async Task ValidateTokenAsync<TRequest>(TRequest request, ServerCallContext context)
    {
        // Allow public methods without token
        if (IsPublicMethod(context.Method))
        {
            return;
        }

        // Extract bearer token from metadata
        var authHeader = context.RequestHeaders.FirstOrDefault(h =>
            h.Key.Equals("authorization", StringComparison.OrdinalIgnoreCase));

        // Try JWT bearer token first
        if (authHeader is not null && !string.IsNullOrWhiteSpace(authHeader.Value))
        {
            await ValidateJwtTokenAsync(authHeader.Value, request, context).ConfigureAwait(false);
            return;
        }

        // Fall back to client certificate (mutual TLS)
        if (TryValidateClientCertificate(context))
        {
            return;
        }

        _logger.LogWarning("gRPC request to {Method} has no valid credentials (JWT or client certificate)", context.Method);
        throw new RpcException(new Status(StatusCode.Unauthenticated, "Authorization required (JWT bearer token or client certificate)"));
    }

    private async Task ValidateJwtTokenAsync<TRequest>(string authHeaderValue, TRequest request, ServerCallContext context)
    {
        var token = ExtractBearerToken(authHeaderValue);
        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogWarning("gRPC request to {Method} has invalid authorization header format", context.Method);
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid token format"));
        }

        try
        {
            var principal = _tokenService.ValidateToken(token);
            var username = _tokenService.GetUsernameFromToken(principal);
            var sessionId = _tokenService.GetSessionIdFromToken(principal);

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(sessionId))
            {
                throw new InvalidOperationException("Token missing required claims");
            }

            EnforceTenantScopeClaims(principal, context.Method);

            // Enforce RBAC permissions
            if (!_rbacService.AuthorizeGrpcCall(principal, context.Method))
            {
                throw new RpcException(new Status(StatusCode.PermissionDenied,
                    $"Insufficient permissions for {context.Method}"));
            }

            await EnforceTenantDatabasePolicyAsync(principal, request, context).ConfigureAwait(false);

            if (_httpContextAccessor.HttpContext is { } httpContext)
            {
                httpContext.User = principal;
            }

            _logger.LogDebug("gRPC {Method} authorized for {Username} via JWT", context.Method, username);
        }
        catch (RpcException)
        {
            throw;
        }
        catch (SecurityTokenValidationException ex)
        {
            _logger.LogWarning(ex, "gRPC token validation failed for {Method}", context.Method);
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Token validation failed"));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "gRPC token validation failed for {Method}", context.Method);
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Token validation failed"));
        }
    }

    private void EnforceTenantScopeClaims(ClaimsPrincipal principal, string method)
    {
        var scopeVersion = principal.GetScopeVersion();
        if (string.IsNullOrWhiteSpace(scopeVersion))
        {
            _logger.LogDebug("gRPC {Method} authorized with legacy JWT claims", method);
            return;
        }

        if (!TenantAwareTokenService.ValidateTenantClaims(principal))
        {
            throw new SecurityTokenValidationException("Tenant scope claims are invalid");
        }

        _logger.LogDebug(
            "gRPC {Method} authorized with tenant scope version {ScopeVersion} for tenant {TenantId}",
            method,
            scopeVersion,
            principal.GetTenantId());
    }

    private async Task EnforceTenantDatabasePolicyAsync<TRequest>(
        ClaimsPrincipal principal,
        TRequest request,
        ServerCallContext context)
    {
        var databaseName = await ResolveDatabaseNameAsync(request, context.CancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            return;
        }

        var requiredPermission = MapMethodToDatabasePermission(context.Method);
        var decision = _tenantAuthorizationPolicyService.AuthorizeDatabaseAccess(
            principal,
            databaseName,
            requiredPermission,
            protocol: "gRPC",
            operation: context.Method);

        if (!decision.IsAllowed)
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied,
                $"Tenant authorization failed: {decision.Code}"));
        }
    }

    private async Task<string?> ResolveDatabaseNameAsync<TRequest>(TRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return null;
        }

        if (TryReadStringProperty(request, "DatabaseName", out var explicitDatabaseName))
        {
            return explicitDatabaseName;
        }

        if (!TryReadStringProperty(request, "SessionId", out var sessionId))
        {
            return null;
        }

        var session = await _sessionManager.GetSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        return session?.DatabaseInstance.Configuration.Name;
    }

    private static bool TryReadStringProperty<TRequest>(TRequest request, string propertyName, out string value)
    {
        value = string.Empty;

        var property = request?.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);

        if (property is null || property.PropertyType != typeof(string))
        {
            return false;
        }

        var rawValue = property.GetValue(request) as string;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        value = rawValue;
        return true;
    }

    private static DatabasePermission MapMethodToDatabasePermission(string method)
    {
        if (method.Contains("ExecuteQuery", StringComparison.OrdinalIgnoreCase)
            || method.Contains("GetSchema", StringComparison.OrdinalIgnoreCase)
            || method.Contains("Graph", StringComparison.OrdinalIgnoreCase)
            || method.Contains("VectorSearch", StringComparison.OrdinalIgnoreCase)
            || method.Contains("GetMetrics", StringComparison.OrdinalIgnoreCase)
            || method.Contains("GetServerInfo", StringComparison.OrdinalIgnoreCase))
        {
            return DatabasePermission.Select;
        }

        if (method.Contains("ExecuteNonQuery", StringComparison.OrdinalIgnoreCase)
            || method.Contains("ExecuteBatch", StringComparison.OrdinalIgnoreCase)
            || method.Contains("Create", StringComparison.OrdinalIgnoreCase)
            || method.Contains("Alter", StringComparison.OrdinalIgnoreCase)
            || method.Contains("Drop", StringComparison.OrdinalIgnoreCase)
            || method.Contains("BeginTransaction", StringComparison.OrdinalIgnoreCase)
            || method.Contains("CommitTransaction", StringComparison.OrdinalIgnoreCase)
            || method.Contains("RollbackTransaction", StringComparison.OrdinalIgnoreCase))
        {
            return DatabasePermission.Insert;
        }

        return DatabasePermission.Connect;
    }

    private bool TryValidateClientCertificate(ServerCallContext context)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var clientCert = httpContext?.Connection.ClientCertificate;
        if (clientCert is null)
        {
            return false;
        }

        var result = _certAuthService.ValidateAndMapCertificate(clientCert);
        if (!result.IsAuthenticated || result.Principal is null)
        {
            _logger.LogWarning("gRPC client certificate rejected for {Method}: {Error}",
                context.Method, result.ErrorMessage);
            return false;
        }

        // Enforce RBAC permissions
        if (!_rbacService.AuthorizeGrpcCall(result.Principal, context.Method))
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied,
                $"Insufficient permissions for {context.Method}"));
        }

        _logger.LogDebug("gRPC {Method} authorized via client certificate", context.Method);
        return true;
    }

    private static bool IsPublicMethod(string method)
    {
        return PublicMethods.Any(pm => method.EndsWith(pm, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ExtractBearerToken(string authHeaderValue)
    {
        const string bearerScheme = "Bearer ";
        if (authHeaderValue.StartsWith(bearerScheme, StringComparison.OrdinalIgnoreCase))
        {
            return authHeaderValue[bearerScheme.Length..].Trim();
        }

        return null;
    }
}
