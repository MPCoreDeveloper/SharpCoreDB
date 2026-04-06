// <copyright file="TenantManagementService.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Grpc.Core;
using Microsoft.Extensions.Logging;
using SharpCoreDB.Server.Core.Tenancy;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpCoreDB.Server.Core.Grpc;

/// <summary>
/// gRPC service implementation for tenant provisioning and management.
/// Handles runtime tenant creation, deletion, and provisioning status queries.
/// C# 14: Uses primary constructor for dependency injection.
/// </summary>
public sealed class TenantManagementGrpcService(
    TenantProvisioningService provisioningService,
    TenantCatalogRepository catalogRepository,
    ILogger<TenantManagementGrpcService> logger) : IAsyncDisposable
{
    /// <summary>
    /// Creates a new tenant with a primary database.
    /// Supports idempotency - retries with same idempotency key return cached result.
    /// </summary>
    public async Task<CreateTenantResponse> CreateTenantAsync(
        string tenantKey,
        string displayName,
        string databasePath,
        string idempotencyKey,
        string? planTier = null,
        string? encryptionKeyReference = null,
        string? metadataJson = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        try
        {
            logger.LogInformation(
                "gRPC CreateTenant request for tenant '{TenantKey}' (idempotency: {IdempotencyKey})",
                tenantKey, idempotencyKey);

            var metadata = metadataJson != null ? JsonDocument.Parse(metadataJson) : null;

            var (tenant, operation) = await provisioningService.CreateTenantAsync(
                tenantKey,
                displayName,
                databasePath,
                idempotencyKey,
                planTier,
                encryptionKeyReference,
                cancellationToken);

            return new CreateTenantResponse
            {
                Status = operation.Status == TenantProvisioningService.OperationStatus.Completed
                    ? CreateTenantResponse.ResponseStatus.Success
                    : CreateTenantResponse.ResponseStatus.ProvisioningFailed,
                TenantId = tenant.TenantId,
                OperationId = operation.OperationId,
                Operation = MapOperationToProto(operation),
                ErrorMessage = operation.ErrorMessage ?? string.Empty
            };
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Invalid operation for tenant '{TenantKey}'", tenantKey);
            return new CreateTenantResponse
            {
                Status = CreateTenantResponse.ResponseStatus.InvalidInput,
                ErrorMessage = ex.Message
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create tenant '{TenantKey}'", tenantKey);
            return new CreateTenantResponse
            {
                Status = CreateTenantResponse.ResponseStatus.InternalError,
                ErrorMessage = $"Internal error: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Deletes a tenant and its associated databases.
    /// Supports idempotency for safe retries.
    /// </summary>
    public async Task<DeleteTenantResponse> DeleteTenantAsync(
        string tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        try
        {
            logger.LogInformation(
                "gRPC DeleteTenant request for tenant '{TenantId}' (idempotency: {IdempotencyKey})",
                tenantId, idempotencyKey);

            var operation = await provisioningService.DeleteTenantAsync(
                tenantId,
                idempotencyKey,
                cancellationToken);

            return new DeleteTenantResponse
            {
                Status = operation.Status == TenantProvisioningService.OperationStatus.Completed
                    ? DeleteTenantResponse.ResponseStatus.Success
                    : DeleteTenantResponse.ResponseStatus.OperationFailed,
                OperationId = operation.OperationId,
                Operation = MapOperationToProto(operation),
                ErrorMessage = operation.ErrorMessage ?? string.Empty
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete tenant '{TenantId}'", tenantId);
            return new DeleteTenantResponse
            {
                Status = DeleteTenantResponse.ResponseStatus.InternalError,
                ErrorMessage = $"Internal error: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Gets the status of a provisioning operation.
    /// </summary>
    public async Task<GetProvisioningStatusResponse> GetProvisioningStatusAsync(
        string operationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);

        try
        {
            var operation = provisioningService.GetOperationStatus(operationId);

            return new GetProvisioningStatusResponse
            {
                Found = operation != null,
                Operation = operation != null ? MapOperationToProto(operation) : null
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get provisioning status for operation '{OperationId}'", operationId);
            return new GetProvisioningStatusResponse { Found = false };
        }
    }

    /// <summary>
    /// Gets a tenant by ID.
    /// </summary>
    public async Task<GetTenantResponse> GetTenantAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        try
        {
            var tenant = await catalogRepository.GetTenantByIdAsync(tenantId, cancellationToken);

            return new GetTenantResponse
            {
                Found = tenant != null,
                Tenant = tenant != null ? MapTenantToProto(tenant) : null
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get tenant '{TenantId}'", tenantId);
            return new GetTenantResponse { Found = false };
        }
    }

    /// <summary>
    /// Lists all tenants, optionally filtered by status.
    /// </summary>
    public async Task<ListTenantsResponse> ListTenantsAsync(
        string? statusFilter = null,
        int pageSize = 100,
        int pageNumber = 1,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var statusFilter_ = string.IsNullOrEmpty(statusFilter)
                ? (TenantStatus?)null
                : Enum.Parse<TenantStatus>(statusFilter);

            var tenants = await catalogRepository.ListTenantsAsync(statusFilter_, cancellationToken);

            // Implement simple pagination
            var skip = (pageNumber - 1) * pageSize;
            var paginated = tenants.Skip(skip).Take(pageSize).ToList();

            return new ListTenantsResponse
            {
                Tenants = paginated.Select(MapTenantToProto).ToList(),
                TotalCount = tenants.Count,
                PageNumber = pageNumber
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list tenants");
            return new ListTenantsResponse { TotalCount = 0 };
        }
    }

    /// <summary>
    /// Updates a tenant's status and/or metadata.
    /// </summary>
    public async Task<UpdateTenantResponse> UpdateTenantAsync(
        string tenantId,
        string? status = null,
        string? metadataJson = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        try
        {
            var newStatus = string.IsNullOrEmpty(status)
                ? (TenantStatus?)null
                : Enum.Parse<TenantStatus>(status);

            var metadata = metadataJson != null ? JsonDocument.Parse(metadataJson) : null;

            await catalogRepository.UpdateTenantAsync(tenantId, newStatus, metadata, cancellationToken);

            var updated = await catalogRepository.GetTenantByIdAsync(tenantId, cancellationToken);

            return new UpdateTenantResponse
            {
                Status = updated != null
                    ? UpdateTenantResponse.ResponseStatus.Success
                    : UpdateTenantResponse.ResponseStatus.NotFound,
                Tenant = updated != null ? MapTenantToProto(updated) : null
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update tenant '{TenantId}'", tenantId);
            return new UpdateTenantResponse
            {
                Status = UpdateTenantResponse.ResponseStatus.InternalError,
                ErrorMessage = $"Internal error: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Maps ProvisioningOperation to proto message.
    /// </summary>
    private static ProvisioningOperationProto MapOperationToProto(
        TenantProvisioningService.ProvisioningOperation operation)
    {
        return new ProvisioningOperationProto
        {
            OperationId = operation.OperationId,
            TenantId = operation.TenantId,
            OperationType = operation.OperationType.ToString(),
            Status = operation.Status.ToString(),
            StartedAtUnix = new DateTimeOffset(operation.StartedAt).ToUnixTimeSeconds(),
            CompletedAtUnix = operation.CompletedAt.HasValue
                ? new DateTimeOffset(operation.CompletedAt.Value).ToUnixTimeSeconds()
                : 0,
            CreatedResources = [..operation.CreatedResources],
            ErrorMessage = operation.ErrorMessage ?? string.Empty
        };
    }

    /// <summary>
    /// Maps TenantInfo to proto message.
    /// </summary>
    private static TenantInfoProto MapTenantToProto(TenantInfo tenant)
    {
        return new TenantInfoProto
        {
            TenantId = tenant.TenantId,
            TenantKey = tenant.TenantKey,
            DisplayName = tenant.DisplayName,
            Status = tenant.Status.ToString(),
            PlanTier = tenant.PlanTier ?? string.Empty,
            CreatedAtUnix = tenant.CreatedAt.HasValue
                ? new DateTimeOffset(tenant.CreatedAt.Value).ToUnixTimeSeconds()
                : 0,
            UpdatedAtUnix = tenant.UpdatedAt.HasValue
                ? new DateTimeOffset(tenant.UpdatedAt.Value).ToUnixTimeSeconds()
                : 0,
            CreatedBy = tenant.CreatedBy ?? string.Empty,
            MetadataJson = tenant.Metadata != null ? JsonSerializer.Serialize(tenant.Metadata) : "{}"
        };
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
    }
}

// Proto message stubs for compilation
// These would normally be generated from .proto file
public class CreateTenantResponse
{
    public enum ResponseStatus
    {
        Success,
        AlreadyExists,
        InvalidInput,
        ProvisioningFailed,
        InternalError
    }

    public ResponseStatus Status { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string OperationId { get; set; } = string.Empty;
    public ProvisioningOperationProto? Operation { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

public class DeleteTenantResponse
{
    public enum ResponseStatus
    {
        Success,
        NotFound,
        OperationFailed,
        InternalError
    }

    public ResponseStatus Status { get; set; }
    public string OperationId { get; set; } = string.Empty;
    public ProvisioningOperationProto? Operation { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

public class GetProvisioningStatusResponse
{
    public ProvisioningOperationProto? Operation { get; set; }
    public bool Found { get; set; }
}

public class GetTenantResponse
{
    public TenantInfoProto? Tenant { get; set; }
    public bool Found { get; set; }
}

public class ListTenantsResponse
{
    public List<TenantInfoProto> Tenants { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
}

public class UpdateTenantResponse
{
    public enum ResponseStatus
    {
        Success,
        NotFound,
        UpdateFailed,
        InternalError
    }

    public ResponseStatus Status { get; set; }
    public TenantInfoProto? Tenant { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

public class ProvisioningOperationProto
{
    public string OperationId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long StartedAtUnix { get; set; }
    public long CompletedAtUnix { get; set; }
    public List<string> CreatedResources { get; set; } = [];
    public string ErrorMessage { get; set; } = string.Empty;
}

public class TenantInfoProto
{
    public string TenantId { get; set; } = string.Empty;
    public string TenantKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string PlanTier { get; set; } = string.Empty;
    public long CreatedAtUnix { get; set; }
    public long UpdatedAtUnix { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string MetadataJson { get; set; } = "{}";
}
