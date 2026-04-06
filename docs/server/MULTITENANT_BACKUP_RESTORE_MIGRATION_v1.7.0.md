# SharpCoreDB Server Tenant Backup Restore and Migration Guide v1.7.0

## Overview
This guide documents tenant-scoped backup, restore, and migration procedures for database-per-tenant deployments.

## Goals
- back up a single tenant without a broad outage
- restore a single tenant with validation before activation
- prepare migration cutover plans between server instances

## REST Endpoints
- `POST /api/v1/tenants/{tenantId}/databases/{databaseName}/backup`
- `POST /api/v1/tenants/{tenantId}/databases/{databaseName}/restore`
- `POST /api/v1/tenants/{tenantId}/databases/{databaseName}/migration-plan`

## Backup Flow
1. resolve tenant database mapping from the tenant catalog
2. export the tenant database file to a backup artifact path
3. record lifecycle events for start/completion/failure
4. retain the artifact in durable operator storage

## Restore Flow
1. resolve tenant database mapping
2. create a rollback copy of the active tenant database when present
3. drain and detach only the tenant database being restored
4. copy the backup artifact to the target restore path
5. validate the restored file by opening it before activation
6. re-register the tenant database and update catalog location metadata if needed
7. roll back to the original file if validation or registration fails

## Migration Planning
Migration plans generate:
- source database path and size metadata
- suggested backup artifact path
- ordered cutover steps
- export, restore, and validation execution hooks for operator automation

## Operational Guidance
- back up `master` separately before control-plane changes
- export tenant backup artifacts to durable storage outside the server host
- validate tenant isolation after restore or migration cutover
- keep encryption key references available before restoring encrypted tenant databases
