// <copyright file="TenantsController.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpCoreDB.Server.Core.Tenancy;
using System.Text.Json;

namespace SharpCoreDB.Server.Core.Api;

/// <summary>
/// REST API controller for tenant management.
/// Exposes provisioning and query endpoints for multi-tenant operations.
/// Requires authentication and appropriate authorization.
/// </summary>
[ApiController]
[Route("api/v1/tenants")]
[Authorize]
[Produces("application/json")]
public sealed class TenantsController(
    TenantProvisioningService provisioningService,
    TenantEncryptionKeyRotationService keyRotationService,
    TenantQuotaEnforcementService quotaEnforcementService,
    TenantCatalogRepository catalogRepository,
    ILogger<TenantsController> logger) : ControllerBase
{
    /// <summary>
    /// Creates a new tenant with a primary database.
    /// POST /api/v1/tenants
    /// </summary>
    /// <param name="request">Tenant creation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Created tenant information with provisioning operation status.</returns>
    [HttpPost]
    [ProducesResponseType(201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(409)]
    [ProducesResponseType(500)]
    public async Task<ActionResult<CreateTenantApiResponse>> CreateTenant(
        [FromBody] CreateTenantApiRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.TenantKey) ||
                string.IsNullOrWhiteSpace(request.DisplayName) ||
                string.IsNullOrWhiteSpace(request.DatabasePath))
            {
                return BadRequest(new ErrorResponse { Message = "Missing required fields" });
            }

            logger.LogInformation(
                "REST CreateTenant request for tenant '{TenantKey}'",
                request.TenantKey);

            var (tenant, operation) = await provisioningService.CreateTenantAsync(
                request.TenantKey,
                request.DisplayName,
                request.DatabasePath,
                request.IdempotencyKey ?? Guid.NewGuid().ToString(),
                request.PlanTier,
                request.EncryptionKeyReference,
                cancellationToken);

            var response = new CreateTenantApiResponse
            {
                TenantId = tenant.TenantId,
                TenantKey = tenant.TenantKey,
                DisplayName = tenant.DisplayName,
                OperationId = operation.OperationId,
                OperationStatus = operation.Status.ToString(),
                CreatedAt = tenant.CreatedAt
            };

            return CreatedAtAction(nameof(GetTenant), new { tenantId = tenant.TenantId }, response);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Invalid operation for tenant");
            return Conflict(new ErrorResponse { Message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create tenant");
            return StatusCode(500,
                new ErrorResponse { Message = "Failed to create tenant" });
        }
    }

    /// <summary>
    /// Rotates a tenant database encryption key reference.
    /// POST /api/v1/tenants/{tenantId}/databases/{databaseName}/encryption/rotate
    /// </summary>
    [HttpPost("{tenantId}/databases/{databaseName}/encryption/rotate")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(500)]
    public async Task<ActionResult<RotateTenantEncryptionKeyApiResponse>> RotateTenantDatabaseEncryptionKey(
        string tenantId,
        string databaseName,
        [FromBody] RotateTenantEncryptionKeyApiRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.NewKeyReference))
        {
            return BadRequest(new ErrorResponse { Message = "NewKeyReference is required" });
        }

        var operation = await keyRotationService.RotateTenantDatabaseKeyAsync(
            tenantId,
            databaseName,
            request.NewKeyReference,
            request.IdempotencyKey ?? Guid.NewGuid().ToString("N"),
            cancellationToken);

        if (operation.Status == TenantEncryptionKeyRotationStatus.Failed)
        {
            return StatusCode(500, new ErrorResponse { Message = operation.ErrorMessage ?? "Key rotation failed" });
        }

        return Ok(new RotateTenantEncryptionKeyApiResponse
        {
            OperationId = operation.OperationId,
            TenantId = operation.TenantId,
            DatabaseName = operation.DatabaseName,
            PreviousKeyReference = operation.PreviousKeyReference,
            NewKeyReference = operation.NewKeyReference,
            Status = operation.Status.ToString(),
            RollbackApplied = operation.RollbackApplied,
            StartedAt = operation.StartedAt,
            CompletedAt = operation.CompletedAt,
        });
    }

    /// <summary>
    /// Gets a tenant by ID.
    /// GET /api/v1/tenants/{tenantId}
    /// </summary>
    /// <param name="tenantId">Tenant ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tenant information.</returns>
    [HttpGet("{tenantId}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<GetTenantApiResponse>> GetTenant(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tenant = await catalogRepository.GetTenantByIdAsync(tenantId, cancellationToken);

            if (tenant == null)
            {
                return NotFound(new ErrorResponse { Message = "Tenant not found" });
            }

            var response = new GetTenantApiResponse
            {
                TenantId = tenant.TenantId,
                TenantKey = tenant.TenantKey,
                DisplayName = tenant.DisplayName,
                Status = tenant.Status.ToString(),
                PlanTier = tenant.PlanTier,
                CreatedAt = tenant.CreatedAt,
                UpdatedAt = tenant.UpdatedAt
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get tenant '{TenantId}'", tenantId);
            return StatusCode(500,
                new ErrorResponse { Message = "Failed to retrieve tenant" });
        }
    }

    /// <summary>
    /// Lists tenants with optional filtering and pagination.
    /// GET /api/v1/tenants?status=Active&pageSize=10&pageNumber=1
    /// </summary>
    /// <param name="status">Optional status filter.</param>
    /// <param name="pageSize">Page size (default 20, max 100).</param>
    /// <param name="pageNumber">Page number (default 1).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of tenants.</returns>
    [HttpGet]
    [ProducesResponseType(200)]
    public async Task<ActionResult<ListTenantsApiResponse>> ListTenants(
        [FromQuery] string? status = null,
        [FromQuery] int pageSize = 20,
        [FromQuery] int pageNumber = 1,
        CancellationToken cancellationToken = default)
    {
        try
        {
            pageSize = Math.Min(Math.Max(pageSize, 1), 100);
            pageNumber = Math.Max(pageNumber, 1);

            var statusFilter = string.IsNullOrEmpty(status)
                ? (TenantStatus?)null
                : Enum.Parse<TenantStatus>(status);

            var allTenants = await catalogRepository.ListTenantsAsync(statusFilter, cancellationToken);

            var paginated = allTenants
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var response = new ListTenantsApiResponse
            {
                Tenants = paginated.Select(t => new TenantSummary
                {
                    TenantId = t.TenantId,
                    TenantKey = t.TenantKey,
                    DisplayName = t.DisplayName,
                    Status = t.Status.ToString(),
                    CreatedAt = t.CreatedAt
                }).ToList(),
                TotalCount = allTenants.Count,
                PageSize = pageSize,
                PageNumber = pageNumber,
                TotalPages = (allTenants.Count + pageSize - 1) / pageSize
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list tenants");
            return StatusCode(500,
                new ErrorResponse { Message = "Failed to list tenants" });
        }
    }

    /// <summary>
    /// Updates a tenant's status and metadata.
    /// PATCH /api/v1/tenants/{tenantId}
    /// </summary>
    /// <param name="tenantId">Tenant ID.</param>
    /// <param name="request">Update request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated tenant information.</returns>
    [HttpPatch("{tenantId}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<GetTenantApiResponse>> UpdateTenant(
        string tenantId,
        [FromBody] UpdateTenantApiRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var newStatus = string.IsNullOrEmpty(request.Status)
                ? (TenantStatus?)null
                : Enum.Parse<TenantStatus>(request.Status);

            var metadata = string.IsNullOrEmpty(request.MetadataJson)
                ? null
                : JsonDocument.Parse(request.MetadataJson);

            await catalogRepository.UpdateTenantAsync(tenantId, newStatus, metadata, cancellationToken);

            var updated = await catalogRepository.GetTenantByIdAsync(tenantId, cancellationToken);

            if (updated == null)
            {
                return NotFound(new ErrorResponse { Message = "Tenant not found" });
            }

            var response = new GetTenantApiResponse
            {
                TenantId = updated.TenantId,
                TenantKey = updated.TenantKey,
                DisplayName = updated.DisplayName,
                Status = updated.Status.ToString(),
                PlanTier = updated.PlanTier,
                CreatedAt = updated.CreatedAt,
                UpdatedAt = updated.UpdatedAt
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update tenant '{TenantId}'", tenantId);
            return StatusCode(500,
                new ErrorResponse { Message = "Failed to update tenant" });
        }
    }

    /// <summary>
    /// Deletes a tenant and its databases.
    /// DELETE /api/v1/tenants/{tenantId}
    /// </summary>
    /// <param name="tenantId">Tenant ID.</param>
    /// <param name="idempotencyKey">Optional idempotency key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Deletion operation status.</returns>
    [HttpDelete("{tenantId}")]
    [ProducesResponseType(202)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<DeleteTenantApiResponse>> DeleteTenant(
        string tenantId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("REST DeleteTenant request for tenant '{TenantId}'", tenantId);

            var operation = await provisioningService.DeleteTenantAsync(
                tenantId,
                idempotencyKey ?? Guid.NewGuid().ToString(),
                cancellationToken);

            var response = new DeleteTenantApiResponse
            {
                OperationId = operation.OperationId,
                OperationStatus = operation.Status.ToString(),
                StartedAt = operation.StartedAt
            };

            return Accepted(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete tenant '{TenantId}'", tenantId);
            return StatusCode(500,
                new ErrorResponse { Message = "Failed to delete tenant" });
        }
    }

    /// <summary>
    /// Gets provisioning operation status.
    /// GET /api/v1/tenants/operations/{operationId}
    /// </summary>
    [HttpGet("operations/{operationId}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public ActionResult<GetOperationStatusApiResponse> GetOperationStatus(string operationId)
    {
        try
        {
            var operation = provisioningService.GetOperationStatus(operationId);

            if (operation == null)
            {
                return NotFound(new ErrorResponse { Message = "Operation not found" });
            }

            var response = new GetOperationStatusApiResponse
            {
                OperationId = operation.OperationId,
                TenantId = operation.TenantId,
                OperationType = operation.OperationType.ToString(),
                Status = operation.Status.ToString(),
                StartedAt = operation.StartedAt,
                CompletedAt = operation.CompletedAt,
                CreatedResources = operation.CreatedResources,
                ErrorMessage = operation.ErrorMessage
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get operation status '{OperationId}'", operationId);
            return StatusCode(500,
                new ErrorResponse { Message = "Failed to retrieve operation status" });
        }
    }

    /// <summary>
    /// Gets effective quota policy for a tenant.
    /// GET /api/v1/tenants/{tenantId}/quotas
    /// </summary>
    [HttpGet("{tenantId}/quotas")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<TenantQuotaApiResponse>> GetTenantQuotas(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        var tenant = await catalogRepository.GetTenantByIdAsync(tenantId, cancellationToken);
        if (tenant is null)
        {
            return NotFound(new ErrorResponse { Message = "Tenant not found" });
        }

        var effective = await quotaEnforcementService.GetEffectiveQuotaPolicyAsync(tenantId, cancellationToken);

        return Ok(new TenantQuotaApiResponse
        {
            TenantId = tenantId,
            MaxActiveSessions = effective.MaxActiveSessions,
            MaxRequestsPerSecond = effective.MaxRequestsPerSecond,
            MaxStorageMb = effective.MaxStorageMb,
            MaxBatchSize = effective.MaxBatchSize,
            UpdatedAt = effective.UpdatedAt,
        });
    }

    /// <summary>
    /// Upserts quota policy overrides for a tenant.
    /// PUT /api/v1/tenants/{tenantId}/quotas
    /// </summary>
    [HttpPut("{tenantId}/quotas")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<TenantQuotaApiResponse>> UpsertTenantQuotas(
        string tenantId,
        [FromBody] UpsertTenantQuotaApiRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.MaxActiveSessions <= 0
            || request.MaxRequestsPerSecond <= 0
            || request.MaxStorageMb <= 0
            || request.MaxBatchSize <= 0)
        {
            return BadRequest(new ErrorResponse { Message = "All quota values must be greater than zero" });
        }

        var tenant = await catalogRepository.GetTenantByIdAsync(tenantId, cancellationToken);
        if (tenant is null)
        {
            return NotFound(new ErrorResponse { Message = "Tenant not found" });
        }

        var policy = TenantQuotaPolicy.Create(
            tenantId,
            request.MaxActiveSessions,
            request.MaxRequestsPerSecond,
            request.MaxStorageMb,
            request.MaxBatchSize);

        await catalogRepository.UpsertTenantQuotaPolicyAsync(policy, cancellationToken);

        var effective = await quotaEnforcementService.GetEffectiveQuotaPolicyAsync(tenantId, cancellationToken);

        return Ok(new TenantQuotaApiResponse
        {
            TenantId = effective.TenantId,
            MaxActiveSessions = effective.MaxActiveSessions,
            MaxRequestsPerSecond = effective.MaxRequestsPerSecond,
            MaxStorageMb = effective.MaxStorageMb,
            MaxBatchSize = effective.MaxBatchSize,
            UpdatedAt = effective.UpdatedAt,
        });
    }
}

// API Request/Response DTOs

/// <summary>
/// Request to create a new tenant.
/// </summary>
public sealed class CreateTenantApiRequest
{
    /// <summary>Unique tenant key.</summary>
    public required string TenantKey { get; init; }

    /// <summary>Display name for the tenant.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Physical path for the primary database file.</summary>
    public required string DatabasePath { get; init; }

    /// <summary>Optional idempotency key for retries.</summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>Optional plan tier.</summary>
    public string? PlanTier { get; init; }

    /// <summary>Optional tenant encryption key reference used during provisioning.</summary>
    public string? EncryptionKeyReference { get; init; }

    /// <summary>Optional metadata as JSON string.</summary>
    public string? MetadataJson { get; init; }
}

/// <summary>
/// Response from create tenant request.
/// </summary>
public sealed class CreateTenantApiResponse
{
    public string TenantId { get; set; } = string.Empty;
    public string TenantKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string OperationId { get; set; } = string.Empty;
    public string OperationStatus { get; set; } = string.Empty;
    public DateTime? CreatedAt { get; set; }
}

/// <summary>
/// Response from get tenant request.
/// </summary>
public sealed class GetTenantApiResponse
{
    public string TenantId { get; set; } = string.Empty;
    public string TenantKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? PlanTier { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Request to update a tenant.
/// </summary>
public sealed class UpdateTenantApiRequest
{
    public string? Status { get; set; }
    public string? MetadataJson { get; set; }
}

/// <summary>
/// Response from delete tenant request.
/// </summary>
public sealed class DeleteTenantApiResponse
{
    public string OperationId { get; set; } = string.Empty;
    public string OperationStatus { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
}

/// <summary>
/// Response listing tenants.
/// </summary>
public sealed class ListTenantsApiResponse
{
    public List<TenantSummary> Tenants { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageSize { get; set; }
    public int PageNumber { get; set; }
    public int TotalPages { get; set; }
}

/// <summary>
/// Summary of a tenant in list response.
/// </summary>
public sealed class TenantSummary
{
    public string TenantId { get; set; } = string.Empty;
    public string TenantKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? CreatedAt { get; set; }
}

/// <summary>
/// Request for tenant database encryption key rotation.
/// </summary>
public sealed class RotateTenantEncryptionKeyApiRequest
{
    /// <summary>New key reference to assign.</summary>
    public required string NewKeyReference { get; init; }

    /// <summary>Optional idempotency key.</summary>
    public string? IdempotencyKey { get; init; }
}

/// <summary>
/// Response for tenant database encryption key rotation.
/// </summary>
public sealed class RotateTenantEncryptionKeyApiResponse
{
    public required string OperationId { get; init; }
    public required string TenantId { get; init; }
    public required string DatabaseName { get; init; }
    public string? PreviousKeyReference { get; init; }
    public required string NewKeyReference { get; init; }
    public required string Status { get; init; }
    public required bool RollbackApplied { get; init; }
    public required DateTime StartedAt { get; init; }
    public required DateTime? CompletedAt { get; init; }
}

/// <summary>
/// Response from get operation status.
/// </summary>
public sealed class GetOperationStatusApiResponse
{
    public string OperationId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<string> CreatedResources { get; set; } = [];
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Generic error response.
/// </summary>
public sealed class ErrorResponse
{
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Request model for upserting tenant quota overrides.
/// </summary>
public sealed class UpsertTenantQuotaApiRequest
{
    public required int MaxActiveSessions { get; init; }
    public required int MaxRequestsPerSecond { get; init; }
    public required long MaxStorageMb { get; init; }
    public required int MaxBatchSize { get; init; }
}

/// <summary>
/// Response model for tenant quota values.
/// </summary>
public sealed class TenantQuotaApiResponse
{
    public required string TenantId { get; init; }
    public required int MaxActiveSessions { get; init; }
    public required int MaxRequestsPerSecond { get; init; }
    public required long MaxStorageMb { get; init; }
    public required int MaxBatchSize { get; init; }
    public required DateTime? UpdatedAt { get; init; }
}
