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
        CREATE TABLE IF NOT EXISTS tenants (
            tenant_id TEXT PRIMARY KEY,
            tenant_key TEXT NOT NULL UNIQUE,
            display_name TEXT NOT NULL,
            status TEXT NOT NULL DEFAULT 'Active',
            plan_tier TEXT,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            created_by TEXT,
            metadata TEXT
        );

        CREATE INDEX IF NOT EXISTS idx_tenants_tenant_key ON tenants(tenant_key);
        CREATE INDEX IF NOT EXISTS idx_tenants_status ON tenants(status);
        CREATE INDEX IF NOT EXISTS idx_tenants_created_at ON tenants(created_at);

        CREATE TABLE IF NOT EXISTS tenant_databases (
            mapping_id TEXT PRIMARY KEY,
            tenant_id TEXT NOT NULL,
            database_name TEXT NOT NULL UNIQUE,
            database_path TEXT NOT NULL,
            is_primary INTEGER NOT NULL DEFAULT 1,
            storage_mode TEXT NOT NULL DEFAULT 'SingleFile',
            encryption_enabled INTEGER NOT NULL DEFAULT 0,
            encryption_key_reference TEXT,
            created_at TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_tenant_db_tenant_id ON tenant_databases(tenant_id);
        CREATE INDEX IF NOT EXISTS idx_tenant_db_database_name ON tenant_databases(database_name);
        CREATE INDEX IF NOT EXISTS idx_tenant_db_is_primary ON tenant_databases(is_primary);

        CREATE TABLE IF NOT EXISTS tenant_quotas (
            tenant_id TEXT PRIMARY KEY,
            max_active_sessions INTEGER NOT NULL,
            max_qps INTEGER NOT NULL,
            max_storage_mb INTEGER NOT NULL,
            max_batch_size INTEGER NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_tenant_quotas_updated ON tenant_quotas(updated_at);

        CREATE TABLE IF NOT EXISTS tenant_lifecycle_events (
            event_id TEXT PRIMARY KEY,
            tenant_id TEXT NOT NULL,
            event_type TEXT NOT NULL,
            event_status TEXT NOT NULL DEFAULT 'Completed',
            event_details TEXT,
            created_at TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_lifecycle_tenant_id ON tenant_lifecycle_events(tenant_id);
        CREATE INDEX IF NOT EXISTS idx_lifecycle_event_type ON tenant_lifecycle_events(event_type);
        CREATE INDEX IF NOT EXISTS idx_lifecycle_created_at ON tenant_lifecycle_events(created_at);
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
