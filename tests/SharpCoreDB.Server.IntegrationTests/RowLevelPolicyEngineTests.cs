// <copyright file="RowLevelPolicyEngineTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharpCoreDB.Server.Core;
using SharpCoreDB.Server.Core.Observability;
using SharpCoreDB.Server.Core.Security;
using SharpCoreDB.Server.Core.Tenancy;

namespace SharpCoreDB.Server.IntegrationTests;

/// <summary>
/// Integration tests for the optional row-level policy engine.
/// Validates tenant isolation for shared-database mode: read filtering,
/// write validation, disabled-mode passthrough, and policy persistence.
/// </summary>
public sealed class RowLevelPolicyEngineTests : IAsyncLifetime
{
    private string _tempRoot = string.Empty;
    private ServerConfiguration _configuration = null!;
    private DatabaseRegistry _databaseRegistry = null!;
    private RowLevelPolicyEngine _policyEngine = null!;
    private RowLevelPolicyRepository _policyRepository = null!;

    public async ValueTask InitializeAsync()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "sharpcoredb-row-policy", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        _configuration = CreateConfiguration(_tempRoot);
        var options = Options.Create(_configuration);

        var metricsCollector = new MetricsCollector("row-policy-tests");
        _databaseRegistry = new DatabaseRegistry(options, NullLogger<DatabaseRegistry>.Instance);

        await _databaseRegistry.InitializeAsync(CancellationToken.None);

        // Create shared table with data for two tenants
        var sharedDb = _databaseRegistry.GetDatabase("shared")
            ?? throw new InvalidOperationException("Shared database is required for row-level policy tests.");

        sharedDb.Database.ExecuteSQL("CREATE TABLE orders (id INTEGER PRIMARY KEY, tenant_id TEXT, product TEXT, amount INTEGER)");
        sharedDb.Database.ExecuteSQL("INSERT INTO orders VALUES (1, 'tenant-a', 'Widget', 100)");
        sharedDb.Database.ExecuteSQL("INSERT INTO orders VALUES (2, 'tenant-b', 'Gadget', 200)");
        sharedDb.Database.ExecuteSQL("INSERT INTO orders VALUES (3, 'tenant-a', 'Sprocket', 50)");
        sharedDb.Database.ExecuteSQL("INSERT INTO orders VALUES (4, 'tenant-b', 'Doohickey', 75)");
        sharedDb.Database.Flush();

        // Initialize policy engine and repository
        _policyEngine = new RowLevelPolicyEngine(NullLogger<RowLevelPolicyEngine>.Instance);

        var masterDb = _databaseRegistry.GetDatabase("master")
            ?? throw new InvalidOperationException("Master database is required for policy persistence tests.");

        _policyRepository = new RowLevelPolicyRepository(masterDb, _policyEngine, NullLogger<RowLevelPolicyRepository>.Instance);
        await _policyRepository.InitializePolicySchemaAsync(CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        await _policyRepository.DisposeAsync();
        await _databaseRegistry.ShutdownAsync(CancellationToken.None);

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
    public void FilterRows_WithEnforcedPolicy_TenantASeesOnlyOwnRows()
    {
        // Arrange
        _policyEngine.RegisterPolicy(RowLevelPolicy.Create("*", "shared", "orders", "tenant_id"));

        var sharedDb = _databaseRegistry.GetDatabase("shared")!;
        var allRows = sharedDb.Database.ExecuteQuery("SELECT * FROM orders");

        // Act
        var filtered = _policyEngine.FilterRows("shared", "orders", "tenant-a", allRows);

        // Assert
        Assert.Equal(2, filtered.Count);
        Assert.All(filtered, row => Assert.Equal("tenant-a", row["tenant_id"]?.ToString()));
    }

    [Fact]
    public void FilterRows_WithEnforcedPolicy_TenantBSeesOnlyOwnRows()
    {
        // Arrange
        _policyEngine.RegisterPolicy(RowLevelPolicy.Create("*", "shared", "orders", "tenant_id"));

        var sharedDb = _databaseRegistry.GetDatabase("shared")!;
        var allRows = sharedDb.Database.ExecuteQuery("SELECT * FROM orders");

        // Act
        var filtered = _policyEngine.FilterRows("shared", "orders", "tenant-b", allRows);

        // Assert
        Assert.Equal(2, filtered.Count);
        Assert.All(filtered, row => Assert.Equal("tenant-b", row["tenant_id"]?.ToString()));
    }

    [Fact]
    public void FilterRows_NoCrosstenantLeakage()
    {
        // Arrange
        _policyEngine.RegisterPolicy(RowLevelPolicy.Create("*", "shared", "orders", "tenant_id"));

        var sharedDb = _databaseRegistry.GetDatabase("shared")!;
        var allRows = sharedDb.Database.ExecuteQuery("SELECT * FROM orders");

        // Act
        var tenantARows = _policyEngine.FilterRows("shared", "orders", "tenant-a", allRows);
        var tenantBRows = _policyEngine.FilterRows("shared", "orders", "tenant-b", allRows);

        // Assert — no row appears in both sets
        var tenantAIds = tenantARows.Select(r => r["id"]?.ToString()).ToHashSet();
        var tenantBIds = tenantBRows.Select(r => r["id"]?.ToString()).ToHashSet();
        Assert.Empty(tenantAIds.Intersect(tenantBIds));
        Assert.Equal(allRows.Count, tenantARows.Count + tenantBRows.Count);
    }

    [Fact]
    public void FilterRows_WithDisabledPolicy_ReturnsAllRows()
    {
        // Arrange
        _policyEngine.RegisterPolicy(RowLevelPolicy.Create("*", "shared", "orders", "tenant_id", RowLevelPolicyMode.Disabled));

        var sharedDb = _databaseRegistry.GetDatabase("shared")!;
        var allRows = sharedDb.Database.ExecuteQuery("SELECT * FROM orders");

        // Act
        var filtered = _policyEngine.FilterRows("shared", "orders", "tenant-a", allRows);

        // Assert — disabled policy returns all rows unfiltered
        Assert.Equal(allRows.Count, filtered.Count);
    }

    [Fact]
    public void FilterRows_WithNoPolicy_ReturnsAllRows()
    {
        // Arrange — no policy registered for this table
        var sharedDb = _databaseRegistry.GetDatabase("shared")!;
        var allRows = sharedDb.Database.ExecuteQuery("SELECT * FROM orders");

        // Act
        var filtered = _policyEngine.FilterRows("shared", "unregistered_table", "tenant-a", allRows);

        // Assert
        Assert.Equal(allRows.Count, filtered.Count);
    }

    [Fact]
    public void ValidateWriteRow_CorrectTenant_Allowed()
    {
        // Arrange
        _policyEngine.RegisterPolicy(RowLevelPolicy.Create("*", "shared", "orders", "tenant_id"));

        var row = new Dictionary<string, object>
        {
            { "id", 10 },
            { "tenant_id", "tenant-a" },
            { "product", "NewWidget" },
            { "amount", 300 }
        };

        // Act
        var decision = _policyEngine.ValidateWriteRow("shared", "orders", "tenant-a", row);

        // Assert
        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void ValidateWriteRow_WrongTenant_Denied()
    {
        // Arrange
        _policyEngine.RegisterPolicy(RowLevelPolicy.Create("*", "shared", "orders", "tenant_id"));

        var row = new Dictionary<string, object>
        {
            { "id", 11 },
            { "tenant_id", "tenant-b" },
            { "product", "StolenWidget" },
            { "amount", 999 }
        };

        // Act
        var decision = _policyEngine.ValidateWriteRow("shared", "orders", "tenant-a", row);

        // Assert
        Assert.False(decision.IsAllowed);
        Assert.Contains("ROW_POLICY_TENANT_MISMATCH", decision.DenialReason);
    }

    [Fact]
    public void ValidateWriteRow_MissingDiscriminator_Denied()
    {
        // Arrange
        _policyEngine.RegisterPolicy(RowLevelPolicy.Create("*", "shared", "orders", "tenant_id"));

        var row = new Dictionary<string, object>
        {
            { "id", 12 },
            { "product", "NoTenantWidget" },
            { "amount", 1 }
        };

        // Act
        var decision = _policyEngine.ValidateWriteRow("shared", "orders", "tenant-a", row);

        // Assert
        Assert.False(decision.IsAllowed);
        Assert.Contains("ROW_POLICY_MISSING_DISCRIMINATOR", decision.DenialReason);
    }

    [Fact]
    public void ValidateWriteStatement_TenantMismatch_Denied()
    {
        // Arrange
        _policyEngine.RegisterPolicy(RowLevelPolicy.Create("*", "shared", "orders", "tenant_id"));

        // Act — INSERT referencing a different tenant
        var decision = _policyEngine.ValidateWriteStatement(
            "shared",
            "INSERT INTO orders VALUES (20, 'tenant-b', 'Stolen', 1)",
            "tenant-a");

        // Assert
        Assert.False(decision.IsAllowed);
        Assert.Contains("ROW_POLICY_WRITE_DENIED", decision.DenialReason);
    }

    [Fact]
    public void ValidateWriteStatement_CorrectTenant_Allowed()
    {
        // Arrange
        _policyEngine.RegisterPolicy(RowLevelPolicy.Create("*", "shared", "orders", "tenant_id"));

        // Act
        var decision = _policyEngine.ValidateWriteStatement(
            "shared",
            "INSERT INTO orders VALUES (30, 'tenant-a', 'OwnWidget', 50)",
            "tenant-a");

        // Assert
        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public async Task PolicyRepository_PersistsAndLoads_Policies()
    {
        // Arrange — create a policy via repository
        var policy = await _policyRepository.CreatePolicyAsync(
            tenantId: "*",
            databaseName: "shared",
            tableName: "customers",
            discriminatorColumn: "tenant_id",
            cancellationToken: CancellationToken.None);

        // Act — verify it's in the engine
        var hasPolicy = _policyEngine.HasEnforcedPolicy("shared", "customers");

        // Assert
        Assert.True(hasPolicy);
        Assert.NotNull(policy.PolicyId);

        // Act — retrieve from repository
        var policies = await _policyRepository.GetPoliciesAsync("shared", CancellationToken.None);

        // Assert
        Assert.Contains(policies, p => p.TableName == "customers" && p.DiscriminatorColumn == "tenant_id");
    }

    [Fact]
    public async Task PolicyRepository_RemovePolicy_DisablesEnforcement()
    {
        // Arrange
        await _policyRepository.CreatePolicyAsync(
            tenantId: "*",
            databaseName: "shared",
            tableName: "temp_table",
            discriminatorColumn: "tenant_id",
            cancellationToken: CancellationToken.None);

        Assert.True(_policyEngine.HasEnforcedPolicy("shared", "temp_table"));

        // Act
        await _policyRepository.RemovePolicyAsync("shared", "temp_table", CancellationToken.None);

        // Assert
        Assert.False(_policyEngine.HasEnforcedPolicy("shared", "temp_table"));
    }

    private static ServerConfiguration CreateConfiguration(string root)
    {
        return new ServerConfiguration
        {
            ServerName = "RowLevelPolicyEngineTests",
            BindAddress = "127.0.0.1",
            GrpcPort = 0,
            DefaultDatabase = "shared",
            Databases =
            [
                new DatabaseInstanceConfiguration
                {
                    Name = "shared",
                    DatabasePath = Path.Combine(root, "shared.db"),
                    StorageMode = "SingleFile",
                },
                new DatabaseInstanceConfiguration
                {
                    Name = "master",
                    DatabasePath = Path.Combine(root, "master.db"),
                    StorageMode = "SingleFile",
                    IsSystemDatabase = true,
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
                JwtSecretKey = "row-level-policy-test-secret-32chars!!",
            },
        };
    }
}
