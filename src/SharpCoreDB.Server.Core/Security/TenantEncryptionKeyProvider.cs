// <copyright file="TenantEncryptionKeyProvider.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.Extensions.Options;

namespace SharpCoreDB.Server.Core.Security;

/// <summary>
/// Provides tenant-scoped encryption key resolution and validation for database provisioning.
/// Designed as an extension point for external KMS integrations.
/// </summary>
public interface ITenantEncryptionKeyProvider
{
    /// <summary>
    /// Resolves encryption material for a tenant database.
    /// </summary>
    /// <param name="tenantId">Tenant identifier.</param>
    /// <param name="databaseName">Database name.</param>
    /// <param name="encryptionKeyReference">Optional explicit key reference override.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resolved key material and metadata.</returns>
    Task<TenantDatabaseEncryptionMaterial> ResolveDatabaseKeyAsync(
        string tenantId,
        string databaseName,
        string? encryptionKeyReference,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default in-process tenant encryption key provider backed by server configuration.
/// </summary>
public sealed class ConfigurationTenantEncryptionKeyProvider(
    IOptions<ServerConfiguration> configuration) : ITenantEncryptionKeyProvider
{
    private readonly ServerConfiguration _config = configuration.Value;

    /// <inheritdoc />
    public async Task<TenantDatabaseEncryptionMaterial> ResolveDatabaseKeyAsync(
        string tenantId,
        string databaseName,
        string? encryptionKeyReference,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        cancellationToken.ThrowIfCancellationRequested();

        var encryptionConfig = _config.Security.TenantEncryption;
        if (!encryptionConfig.Enabled)
        {
            return TenantDatabaseEncryptionMaterial.Disabled();
        }

        var effectiveReference = ResolveKeyReference(encryptionConfig, tenantId, encryptionKeyReference);

        if (string.IsNullOrWhiteSpace(effectiveReference))
        {
            if (encryptionConfig.RequireDedicatedTenantKey)
            {
                throw new InvalidOperationException(
                    $"No encryption key reference configured for tenant '{tenantId}'.");
            }

            return TenantDatabaseEncryptionMaterial.Disabled();
        }

        if (!encryptionConfig.KeyMaterialByReference.TryGetValue(effectiveReference, out var keyMaterial)
            || string.IsNullOrWhiteSpace(keyMaterial))
        {
            throw new KeyNotFoundException(
                $"Encryption key material not found for reference '{effectiveReference}'.");
        }

        return new TenantDatabaseEncryptionMaterial(
            EncryptionEnabled: true,
            KeyReference: effectiveReference,
            KeyMaterial: keyMaterial,
            ProviderName: encryptionConfig.DefaultProvider);
    }

    private static string? ResolveKeyReference(
        TenantEncryptionConfiguration encryptionConfig,
        string tenantId,
        string? explicitReference)
    {
        if (!string.IsNullOrWhiteSpace(explicitReference))
        {
            return explicitReference.Trim();
        }

        if (encryptionConfig.DefaultKeyReferenceByTenant.TryGetValue(tenantId, out var tenantReference)
            && !string.IsNullOrWhiteSpace(tenantReference))
        {
            return tenantReference.Trim();
        }

        return encryptionConfig.DefaultKeyReference;
    }
}

/// <summary>
/// Resolved tenant database encryption material.
/// </summary>
public sealed record TenantDatabaseEncryptionMaterial(
    bool EncryptionEnabled,
    string? KeyReference,
    string? KeyMaterial,
    string ProviderName)
{
    /// <summary>
    /// Creates a material object representing disabled encryption.
    /// </summary>
    public static TenantDatabaseEncryptionMaterial Disabled()
    {
        return new TenantDatabaseEncryptionMaterial(
            EncryptionEnabled: false,
            KeyReference: null,
            KeyMaterial: null,
            ProviderName: "none");
    }
}
