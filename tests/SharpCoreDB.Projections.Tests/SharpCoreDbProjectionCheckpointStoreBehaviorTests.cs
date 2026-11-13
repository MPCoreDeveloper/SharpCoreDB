// <copyright file="SharpCoreDbProjectionCheckpointStoreBehaviorTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.Projections.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Interfaces;

/// <summary>
/// Behaviour of <see cref="SharpCoreDbProjectionCheckpointStore"/> beyond the happy-path persistence test:
/// argument guards, parameter binding, field round-trip, SQL-literal escaping, table-name validation and the
/// null-timestamp fallback.
/// Added with the mutation baseline — 29 mutants survived the existing suite, which asserted only
/// <c>GlobalSequence</c>: the projection name, database id, tenant id and timestamp could all be mapped
/// wrongly (or the tenant/database parameters bound in the wrong order) without a red test.
/// </summary>
public sealed class SharpCoreDbProjectionCheckpointStoreBehaviorTests : IAsyncDisposable
{
    private const string TableName = "scdb_projection_checkpoints";

    private readonly DatabaseFactory _factory;
    private readonly List<IDatabase> _dbs = [];
    private readonly List<string> _paths = [];

    public SharpCoreDbProjectionCheckpointStoreBehaviorTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
    }

    [Fact]
    public void Constructor_WithBlankTableName_ThrowsArgumentException()
    {
        var db = CreateDatabase();

        Assert.Throws<ArgumentException>(() => new SharpCoreDbProjectionCheckpointStore(db, "   "));
    }

    [Fact]
    public void Constructor_WithIllegalTableNameCharacters_ThrowsArgumentException()
    {
        var db = CreateDatabase();

        Assert.Throws<ArgumentException>(() => new SharpCoreDbProjectionCheckpointStore(db, "bad-table; DROP"));
    }

    [Fact]
    public async Task SaveCheckpointAsync_ThenGet_RoundTripsEveryField()
    {
        // Arrange — a whole-second timestamp so the round-trip is exact
        var (_, store) = CreateStore();
        var at = new DateTimeOffset(2026, 5, 4, 12, 30, 15, TimeSpan.Zero);
        var saved = new ProjectionCheckpoint("OrdersProjection", "main", "tenant-a", 123, at);

        // Act
        await store.SaveCheckpointAsync(saved);
        var loaded = await store.GetCheckpointAsync("OrdersProjection", "main", "tenant-a");

        // Assert — every field, not just the sequence
        Assert.NotNull(loaded);
        Assert.Equal("OrdersProjection", loaded.Value.ProjectionName);
        Assert.Equal("main", loaded.Value.DatabaseId);
        Assert.Equal("tenant-a", loaded.Value.TenantId);
        Assert.Equal(123, loaded.Value.GlobalSequence);
        Assert.Equal(at, loaded.Value.UpdatedAtUtc);
    }

    [Fact]
    public async Task GetCheckpointAsync_ForUnknownKey_ReturnsNull()
    {
        var (_, store) = CreateStore();

        var loaded = await store.GetCheckpointAsync("Missing", "main", "tenant-a");

        Assert.Null(loaded);
    }

    [Fact]
    public async Task GetCheckpointAsync_DistinguishesDatabaseAndTenant()
    {
        // Arrange — three rows that differ only in the bound parameters
        var (_, store) = CreateStore();
        await store.SaveCheckpointAsync(new ProjectionCheckpoint("Orders", "db-1", "tenant-1", 11, DateTimeOffset.UtcNow));
        await store.SaveCheckpointAsync(new ProjectionCheckpoint("Orders", "db-1", "tenant-2", 22, DateTimeOffset.UtcNow));
        await store.SaveCheckpointAsync(new ProjectionCheckpoint("Orders", "db-2", "tenant-1", 33, DateTimeOffset.UtcNow));

        // Act / Assert — a mis-bound parameter would return a neighbour's row
        Assert.Equal(11, (await store.GetCheckpointAsync("Orders", "db-1", "tenant-1"))?.GlobalSequence);
        Assert.Equal(22, (await store.GetCheckpointAsync("Orders", "db-1", "tenant-2"))?.GlobalSequence);
        Assert.Equal(33, (await store.GetCheckpointAsync("Orders", "db-2", "tenant-1"))?.GlobalSequence);
    }

    [Fact]
    public async Task SaveCheckpointAsync_WhenCalledTwice_KeepsLatestSequence()
    {
        // Arrange
        var (_, store) = CreateStore();
        await store.SaveCheckpointAsync(new ProjectionCheckpoint("Orders", "main", "tenant-a", 10, DateTimeOffset.UtcNow));

        // Act
        await store.SaveCheckpointAsync(new ProjectionCheckpoint("Orders", "main", "tenant-a", 20, DateTimeOffset.UtcNow));
        var loaded = await store.GetCheckpointAsync("Orders", "main", "tenant-a");

        // Assert
        Assert.Equal(20, loaded?.GlobalSequence);
    }

    [Fact]
    public async Task SaveCheckpointAsync_WithApostropheInName_RoundTrips()
    {
        // Arrange — the value is interpolated into SQL, so the escaping must survive a round-trip
        var (_, store) = CreateStore();
        const string tricky = "Orders'Projection";

        // Act
        await store.SaveCheckpointAsync(new ProjectionCheckpoint(tricky, "main", "tenant-a", 7, DateTimeOffset.UtcNow));
        var loaded = await store.GetCheckpointAsync(tricky, "main", "tenant-a");

        // Assert
        Assert.Equal(tricky, loaded?.ProjectionName);
        Assert.Equal(7, loaded?.GlobalSequence);
    }

    [Fact]
    public async Task GetCheckpointAsync_WhenUpdatedAtIsNull_FallsBackToUtcNow()
    {
        // Arrange — a row written directly, with a NULL timestamp
        var (db, store) = CreateStore();
        db.ExecuteSQL($"CREATE TABLE {TableName} (projection_name TEXT, database_id TEXT, tenant_id TEXT, global_sequence LONG, updated_at_utc TEXT)");
        db.ExecuteSQL($"INSERT INTO {TableName} VALUES ('Orders', 'main', 'tenant-a', 5, NULL)");
        db.Flush();

        // Act
        var loaded = await store.GetCheckpointAsync("Orders", "main", "tenant-a");

        // Assert — sequence still parses, timestamp falls back instead of throwing
        Assert.Equal(5, loaded?.GlobalSequence);
        Assert.True(loaded!.Value.UpdatedAtUtc > DateTimeOffset.UtcNow.AddMinutes(-5),
            $"Expected the UtcNow fallback, got {loaded!.Value.UpdatedAtUtc:O}.");
    }

    [Fact]
    public async Task GetCheckpointAsync_WithACustomTableName_UsesThatTable()
    {
        // Arrange
        var db = CreateDatabase();
        var store = new SharpCoreDbProjectionCheckpointStore(db, "custom_checkpoints");

        // Act
        await store.SaveCheckpointAsync(new ProjectionCheckpoint("Orders", "main", "tenant-a", 9, DateTimeOffset.UtcNow));
        var loaded = await store.GetCheckpointAsync("Orders", "main", "tenant-a");

        // Assert — the custom table exists and holds the row
        Assert.Equal(9, loaded?.GlobalSequence);
        Assert.True(db.TryGetTable("custom_checkpoints", out _));
    }

    [Fact]
    public void GetCheckpointAsync_WhenCancelled_ThrowsOperationCanceled()
    {
        var (_, store) = CreateStore();

        Assert.Throws<OperationCanceledException>(() =>
        {
            _ = store.GetCheckpointAsync("Orders", "main", "tenant-a", new CancellationToken(canceled: true));
        });
    }

    [Fact]
    public void SaveCheckpointAsync_WhenCancelled_ThrowsOperationCanceled()
    {
        var (_, store) = CreateStore();

        Assert.Throws<OperationCanceledException>(() =>
        {
            _ = store.SaveCheckpointAsync(
                new ProjectionCheckpoint("Orders", "main", "tenant-a", 1, DateTimeOffset.UtcNow),
                new CancellationToken(canceled: true));
        });
    }

    [Fact]
    public void GetCheckpointAsync_WithBlankProjectionName_ThrowsArgumentException()
    {
        var (_, store) = CreateStore();

        Assert.Throws<ArgumentException>(() => { _ = store.GetCheckpointAsync(" ", "main", "tenant-a"); });
    }

    [Fact]
    public void GetCheckpointAsync_WithBlankDatabaseId_ThrowsArgumentException()
    {
        var (_, store) = CreateStore();

        Assert.Throws<ArgumentException>(() => { _ = store.GetCheckpointAsync("Orders", "", "tenant-a"); });
    }

    [Fact]
    public void GetCheckpointAsync_WithBlankTenantId_ThrowsArgumentException()
    {
        var (_, store) = CreateStore();

        Assert.Throws<ArgumentException>(() => { _ = store.GetCheckpointAsync("Orders", "main", "  "); });
    }

    [Fact]
    public void SaveCheckpointAsync_WithBlankProjectionName_ThrowsArgumentException()
    {
        var (_, store) = CreateStore();

        Assert.Throws<ArgumentException>(() =>
        {
            _ = store.SaveCheckpointAsync(new ProjectionCheckpoint(" ", "main", "tenant-a", 1, DateTimeOffset.UtcNow));
        });
    }

    [Fact]
    public void SaveCheckpointAsync_WithBlankDatabaseId_ThrowsArgumentException()
    {
        var (_, store) = CreateStore();

        Assert.Throws<ArgumentException>(() =>
        {
            _ = store.SaveCheckpointAsync(new ProjectionCheckpoint("Orders", "", "tenant-a", 1, DateTimeOffset.UtcNow));
        });
    }

    [Fact]
    public void SaveCheckpointAsync_WithBlankTenantId_ThrowsArgumentException()
    {
        var (_, store) = CreateStore();

        Assert.Throws<ArgumentException>(() =>
        {
            _ = store.SaveCheckpointAsync(new ProjectionCheckpoint("Orders", "main", "  ", 1, DateTimeOffset.UtcNow));
        });
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var db in _dbs)
        {
            await db.DisposeAsync();
        }

        foreach (var path in _paths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
                // Ignore cleanup errors.
            }
        }
    }

    private IDatabase CreateDatabase()
    {
        var path = NewPath();
        var db = _factory.Create(path, "projection-checkpoint-test-password");
        _dbs.Add(db);
        return db;
    }

    private (IDatabase Db, SharpCoreDbProjectionCheckpointStore Store) CreateStore()
    {
        var db = CreateDatabase();
        return (db, new SharpCoreDbProjectionCheckpointStore(db));
    }

    private string NewPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"SharpCoreDB_Checkpoint_{Guid.NewGuid():N}");
        _paths.Add(path);
        return path;
    }
}
