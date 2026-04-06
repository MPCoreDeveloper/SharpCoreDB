// <copyright file="TenantCatalogSchema.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.Server.Core.Tenancy;

/// <summary>
/// SQL schema definitions for multi-tenant catalog in the master database.
/// Contains tenant metadata, database mappings, and lifecycle tracking.
/// </summary>
public static class TenantCatalogSchema
{
    /// <summary>
    /// Creates the tenant catalog tables in master database.
    /// Includes indices for efficient lookups.
    /// </summary>
    public static readonly string CreateCatalogTables = """
        -- Tenant catalog table: core tenant metadata
        IF NOT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'tenants')
        BEGIN
            CREATE TABLE tenants (
                tenant_id NVARCHAR(128) PRIMARY KEY,
                tenant_key NVARCHAR(256) NOT NULL UNIQUE,
                display_name NVARCHAR(512) NOT NULL,
                status NVARCHAR(50) NOT NULL DEFAULT 'Active',
                plan_tier NVARCHAR(50),
                created_at DATETIME NOT NULL DEFAULT GETUTCDATE(),
                updated_at DATETIME NOT NULL DEFAULT GETUTCDATE(),
                created_by NVARCHAR(256),
                metadata NVARCHAR(MAX)
            );

            CREATE INDEX idx_tenants_tenant_key ON tenants(tenant_key);
            CREATE INDEX idx_tenants_status ON tenants(status);
            CREATE INDEX idx_tenants_created_at ON tenants(created_at);
        END;

        -- Tenant-to-database mapping table
        IF NOT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'tenant_databases')
        BEGIN
            CREATE TABLE tenant_databases (
                mapping_id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
                tenant_id NVARCHAR(128) NOT NULL,
                database_name NVARCHAR(128) NOT NULL UNIQUE,
                database_path NVARCHAR(512) NOT NULL,
                is_primary BIT NOT NULL DEFAULT 1,
                storage_mode NVARCHAR(50) NOT NULL DEFAULT 'SingleFile',
                encryption_enabled BIT NOT NULL DEFAULT 0,
                encryption_key_reference NVARCHAR(256),
                created_at DATETIME NOT NULL DEFAULT GETUTCDATE(),
                FOREIGN KEY (tenant_id) REFERENCES tenants(tenant_id) ON DELETE CASCADE
            );

            CREATE INDEX idx_tenant_db_tenant_id ON tenant_databases(tenant_id);
            CREATE INDEX idx_tenant_db_database_name ON tenant_databases(database_name);
            CREATE INDEX idx_tenant_db_is_primary ON tenant_databases(is_primary);
        END;

        -- Tenant quota policies
        IF NOT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'tenant_quotas')
        BEGIN
            CREATE TABLE tenant_quotas (
                tenant_id NVARCHAR(128) PRIMARY KEY,
                max_active_sessions INT NOT NULL,
                max_qps INT NOT NULL,
                max_storage_mb BIGINT NOT NULL,
                max_batch_size INT NOT NULL,
                updated_at DATETIME NOT NULL DEFAULT GETUTCDATE(),
                FOREIGN KEY (tenant_id) REFERENCES tenants(tenant_id) ON DELETE CASCADE
            );

            CREATE INDEX idx_tenant_quotas_updated ON tenant_quotas(updated_at);
        END;

        -- Tenant lifecycle events for auditing and debugging
        IF NOT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'tenant_lifecycle_events')
        BEGIN
            CREATE TABLE tenant_lifecycle_events (
                event_id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
                tenant_id NVARCHAR(128) NOT NULL,
                event_type NVARCHAR(100) NOT NULL,
                event_status NVARCHAR(50) NOT NULL DEFAULT 'Completed',
                event_details NVARCHAR(MAX),
                created_at DATETIME NOT NULL DEFAULT GETUTCDATE(),
                FOREIGN KEY (tenant_id) REFERENCES tenants(tenant_id) ON DELETE CASCADE
            );

            CREATE INDEX idx_lifecycle_tenant_id ON tenant_lifecycle_events(tenant_id);
            CREATE INDEX idx_lifecycle_event_type ON tenant_lifecycle_events(event_type);
            CREATE INDEX idx_lifecycle_created_at ON tenant_lifecycle_events(created_at);
        END;
        """;

    /// <summary>
    /// Drops all tenant catalog tables and indices.
    /// </summary>
    public static readonly string DropCatalogTables = """
        -- Drop tables in reverse dependency order
        IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'tenant_lifecycle_events')
            DROP TABLE tenant_lifecycle_events;

        IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'tenant_quotas')
            DROP TABLE tenant_quotas;

        IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'tenant_databases')
            DROP TABLE tenant_databases;

        IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'tenants')
            DROP TABLE tenants;
        """;
}
