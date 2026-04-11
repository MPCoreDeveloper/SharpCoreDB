// <copyright file="DatabaseGrantsRepositoryTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharpCoreDB.Server.Core;
using SharpCoreDB.Server.Core.Observability;
using SharpCoreDB.Server.Core.Security;

namespace SharpCoreDB.Server.IntegrationTests;

/// <summary>
/// Integration tests for per-database grants repository behavior.
/// </summary>
public sealed class DatabaseGrantsRepositoryTests : IAsyncLifetime
{
    private string _tempRoot = string.Empty;
    private DatabaseRegistry _databaseRegistry = null!;
    private DatabaseGrantsRepository _grantsRepository = null!;
    private TenantSecurityAuditStore _securityAuditStore = null!;

    public async ValueTask InitializeAsync()
    {
        _tempRoot = Path.Combine(
            Path.GetTempPath(),
            "sharpcoredb-grants-tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_tempRoot);

        var options = Options.Create(CreateConfiguration(_tempRoot));
        _databaseRegistry = new DatabaseRegistry(options, NullLogger<DatabaseRegistry>.Instance);
        await _databaseRegistry.InitializeAsync(CancellationToken.None);

        var masterDatabase = _databaseRegistry.GetDatabase("master")
            ?? throw new InvalidOperationException("Master database must be initialized for grants tests.");

        _securityAuditStore = new TenantSecurityAuditStore();
        var auditService = new TenantSecurityAuditService(
            _securityAuditStore,
            NullLogger<TenantSecurityAuditService>.Instance);

        _grantsRepository = new DatabaseGrantsRepository(
            masterDatabase,
            auditService,
            NullLogger<DatabaseGrantsRepository>.Instance);

        await _grantsRepository.InitializeGrantsSchemaAsync(CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        await _grantsRepository.DisposeAsync();

        if (_databaseRegistry is not null)
        {
            await _databaseRegistry.ShutdownAsync(CancellationToken.None);
        }

        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public async Task HasPermissionAsync_WhenGrantedForDatabaseA_AllowsDatabaseAAndDeniesDatabaseB()
    {
        await _grantsRepository.GrantPermissionAsync(
            tenantId: "tenant-a",
            databaseName: "db-a",
            principal: "user-a",
            permission: DatabasePermission.Select,
            cancellationToken: CancellationToken.None);

        var allowed = await _grantsRepository.HasPermissionAsync(
            "tenant-a",
            "db-a",
            "user-a",
            DatabasePermission.Select,
            CancellationToken.None);

        var denied = await _grantsRepository.HasPermissionAsync(
            "tenant-a",
            "db-b",
            "user-a",
            DatabasePermission.Select,
            CancellationToken.None);

        Assert.True(allowed);
        Assert.False(denied);
    }

    [Fact]
    public async Task RevokeGrantAsync_WhenRevoked_DeniesPermission()
    {
        var grant = await _grantsRepository.GrantPermissionAsync(
            tenantId: "tenant-b",
            databaseName: "db-b",
            principal: "user-b",
            permission: DatabasePermission.Insert,
            cancellationToken: CancellationToken.None);

        await _grantsRepository.RevokeGrantAsync(grant.GrantId, CancellationToken.None);

        var allowed = await _grantsRepository.HasPermissionAsync(
            "tenant-b",
            "db-b",
            "user-b",
            DatabasePermission.Insert,
            CancellationToken.None);

        Assert.False(allowed);
    }

    [Fact]
    public async Task GrantPermissionAsync_WhenGranted_EmitsSecurityAuditEvent()
    {
        _ = await _grantsRepository.GrantPermissionAsync(
            tenantId: "tenant-c",
            databaseName: "db-c",
            principal: "user-c",
            permission: DatabasePermission.Connect,
            cancellationToken: CancellationToken.None);

        var events = _securityAuditStore.GetRecent(10);

        Assert.True(events.Any(e => e.EventType == TenantSecurityEventType.GrantChanged));
    }

    private static ServerConfiguration CreateConfiguration(string root)
    {
        return new ServerConfiguration
        {
            ServerName = "DatabaseGrantsTests",
            BindAddress = "127.0.0.1",
            GrpcPort = 0,
            DefaultDatabase = "master",
            Databases =
            [
                new DatabaseInstanceConfiguration
                {
                    Name = "master",
                    DatabasePath = Path.Combine(root, "master.db"),
                    StorageMode = "SingleFile",
                    IsSystemDatabase = true,
                    ConnectionPoolSize = 5,
                },
            ],
            SystemDatabases = new SystemDatabasesConfiguration
            {
                Enabled = false,
                MasterDatabaseName = "master",
            },
            Security = new SecurityConfiguration
            {
                TlsCertificatePath = "dummy.pem",
                TlsPrivateKeyPath = "dummy.key",
                JwtSecretKey = "integration-test-secret-key-32chars!!",
            },
        };
    }
}
