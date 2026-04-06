// <copyright file="TenantQuotaEnforcementService.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpCoreDB.Server.Core.Observability;

namespace SharpCoreDB.Server.Core.Tenancy;

/// <summary>
/// Evaluates and enforces per-tenant quota policies at runtime.
/// </summary>
public sealed class TenantQuotaEnforcementService(
    IOptions<ServerConfiguration> configuration,
    MetricsCollector metricsCollector,
    ILogger<TenantQuotaEnforcementService> logger,
    TenantCatalogRepository? catalogRepository = null)
{
    private readonly TenantCatalogRepository? _catalogRepository = catalogRepository;
    private readonly ServerConfiguration _config = configuration.Value;
    private readonly MetricsCollector _metricsCollector = metricsCollector;
    private readonly ILogger<TenantQuotaEnforcementService> _logger = logger;
    private readonly Lock _rateLock = new();
    private readonly Dictionary<string, TenantRequestWindow> _requestWindows = [];

    /// <summary>
    /// Ensures a tenant can create a new session.
    /// </summary>
    public async Task EnsureSessionQuotaAsync(
        string tenantId,
        int currentActiveSessions,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        if (!_config.TenantQuotas.Enabled)
        {
            return;
        }

        var policy = await GetEffectiveQuotaPolicyAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (currentActiveSessions >= policy.MaxActiveSessions)
        {
            ThrowQuotaExceeded(
                tenantId,
                quotaType: "active_sessions",
                operation: "CreateSession",
                code: "TENANT_MAX_ACTIVE_SESSIONS_EXCEEDED",
                message: $"Tenant '{tenantId}' exceeded max active sessions ({policy.MaxActiveSessions}).");
        }
    }

    /// <summary>
    /// Ensures tenant request rate and batch-size limits before processing.
    /// </summary>
    public async Task EnsureRequestQuotaAsync(
        string tenantId,
        string operation,
        int? batchSize = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        if (!_config.TenantQuotas.Enabled)
        {
            return;
        }

        var policy = await GetEffectiveQuotaPolicyAsync(tenantId, cancellationToken).ConfigureAwait(false);

        if (batchSize.HasValue && batchSize.Value > policy.MaxBatchSize)
        {
            ThrowQuotaExceeded(
                tenantId,
                quotaType: "batch_size",
                operation,
                code: "TENANT_MAX_BATCH_SIZE_EXCEEDED",
                message: $"Tenant '{tenantId}' exceeded max batch size ({policy.MaxBatchSize}).");
        }

        var now = DateTime.UtcNow;
        lock (_rateLock)
        {
            if (!_requestWindows.TryGetValue(tenantId, out var window) || (now - window.WindowStartUtc).TotalSeconds >= 1)
            {
                _requestWindows[tenantId] = new TenantRequestWindow(now, 1);
                return;
            }

            if (window.Count >= policy.MaxRequestsPerSecond)
            {
                ThrowQuotaExceeded(
                    tenantId,
                    quotaType: "qps",
                    operation,
                    code: "TENANT_MAX_QPS_EXCEEDED",
                    message: $"Tenant '{tenantId}' exceeded max requests per second ({policy.MaxRequestsPerSecond}).");
            }

            _requestWindows[tenantId] = window with { Count = window.Count + 1 };
        }
    }

    /// <summary>
    /// Ensures current tenant storage usage is below configured limit.
    /// </summary>
    public async Task EnsureStorageQuotaAsync(
        string tenantId,
        string databasePath,
        string operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        if (!_config.TenantQuotas.Enabled)
        {
            return;
        }

        var policy = await GetEffectiveQuotaPolicyAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (!File.Exists(databasePath))
        {
            return;
        }

        var fileInfo = new FileInfo(databasePath);
        var usageMb = fileInfo.Length / (1024d * 1024d);
        if (usageMb > policy.MaxStorageMb)
        {
            ThrowQuotaExceeded(
                tenantId,
                quotaType: "storage_mb",
                operation,
                code: "TENANT_MAX_STORAGE_EXCEEDED",
                message: $"Tenant '{tenantId}' exceeded max storage ({policy.MaxStorageMb} MB).");
        }
    }

    /// <summary>
    /// Gets the effective quota policy for a tenant.
    /// </summary>
    public async Task<TenantQuotaPolicy> GetEffectiveQuotaPolicyAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        if (_catalogRepository is not null)
        {
            var explicitPolicy = await _catalogRepository.GetTenantQuotaPolicyAsync(tenantId, cancellationToken).ConfigureAwait(false);
            if (explicitPolicy is not null)
            {
                return explicitPolicy;
            }

            var tenant = await _catalogRepository.GetTenantByIdAsync(tenantId, cancellationToken).ConfigureAwait(false);
            var planTier = tenant?.PlanTier;

            if (!string.IsNullOrWhiteSpace(planTier)
                && _config.TenantQuotas.PlanDefaults.TryGetValue(planTier, out var planPolicy))
            {
                return new TenantQuotaPolicy(
                    TenantId: tenantId,
                    MaxActiveSessions: planPolicy.MaxActiveSessions,
                    MaxRequestsPerSecond: planPolicy.MaxRequestsPerSecond,
                    MaxStorageMb: planPolicy.MaxStorageMb,
                    MaxBatchSize: planPolicy.MaxBatchSize,
                    UpdatedAt: DateTime.UtcNow);
            }
        }

        var defaults = _config.TenantQuotas;
        return new TenantQuotaPolicy(
            TenantId: tenantId,
            MaxActiveSessions: defaults.DefaultMaxActiveSessions,
            MaxRequestsPerSecond: defaults.DefaultMaxRequestsPerSecond,
            MaxStorageMb: defaults.DefaultMaxStorageMb,
            MaxBatchSize: defaults.DefaultMaxBatchSize,
            UpdatedAt: DateTime.UtcNow);
    }

    private void ThrowQuotaExceeded(
        string tenantId,
        string quotaType,
        string operation,
        string code,
        string message)
    {
        _metricsCollector.RecordTenantQuotaThrottle(tenantId, quotaType, operation, code);

        _logger.LogWarning(
            "Tenant quota exceeded [{Code}] tenant={TenantId} quota={QuotaType} operation={Operation}: {Message}",
            code,
            tenantId,
            quotaType,
            operation,
            message);

        throw new TenantQuotaExceededException(code, message);
    }

    private sealed record TenantRequestWindow(DateTime WindowStartUtc, int Count);
}

/// <summary>
/// Exception thrown when tenant quota is exceeded.
/// </summary>
public sealed class TenantQuotaExceededException(string code, string message)
    : InvalidOperationException(message)
{
    /// <summary>
    /// Quota error code.
    /// </summary>
    public string Code { get; } = code;
}
