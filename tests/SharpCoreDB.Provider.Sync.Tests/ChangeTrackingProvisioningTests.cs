using Xunit;
using SharpCoreDB;
using SharpCoreDB.Provider.Sync.ChangeTracking;
using SharpCoreDB.Services;
using SharpCoreDB.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace SharpCoreDB.Provider.Sync.Tests;

/// <summary>
/// Tests for change tracking provisioning and trigger execution.
/// </summary>
public sealed class ChangeTrackingProvisioningTests : IDisposable
{
    private readonly string _dbPath;
    private readonly IServiceProvider _serviceProvider;
    private readonly IDatabase _db;
    private readonly TrackingTableBuilder _trackingBuilder;
    private readonly ChangeTrackingManager _trackingManager;

    public ChangeTrackingProvisioningTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"test_sync_{Guid.NewGuid():N}.scdb");

        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _serviceProvider = services.BuildServiceProvider();

        var factory = _serviceProvider.GetRequiredService<DatabaseFactory>();
        _db = factory.Create(_dbPath, "test", isReadOnly: false);

        var dialect = new SqliteDialect();
        _trackingBuilder = new TrackingTableBuilder(dialect);
        _trackingManager = new ChangeTrackingManager(_trackingBuilder, dialect);
    }

    public void Dispose()
    {
        if (_db is IDisposable disposable)
        {
            disposable.Dispose();
        }

        if (_serviceProvider is IDisposable serviceDisposable)
        {
            serviceDisposable.Dispose();
        }

        if (Directory.Exists(_dbPath))
        {
            Directory.Delete(_dbPath, true);
        }
    }

    [Fact]
    public async Task ProvisionTrackingAsync_CreatesTrackingTableAndTriggers()
    {
        // Arrange
        _db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL)");

        // Act
        await _trackingManager.ProvisionTrackingAsync(_db, "users");

        // Assert
        var tables = _db.GetTables();
        Assert.Contains(tables, t => t.Name == "users_tracking");

        var triggers = _db.ExecuteQuery("SELECT name FROM sqlite_master WHERE type='trigger' AND name LIKE 'trg_users_%'");
        Assert.Equal(3, triggers.Count); // INSERT, UPDATE, DELETE triggers
    }

    [Fact]
    public async Task ProvisionTracking_AfterInsert_CreatesTrackingRecord()
    {
        // Arrange
        _db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL)");
        await _trackingManager.ProvisionTrackingAsync(_db, "users");

        // Act
        _db.ExecuteSQL("INSERT INTO users (id, name) VALUES (1, 'Alice')");

        // Assert
        var trackingRows = _db.ExecuteQuery("SELECT * FROM users_tracking WHERE id = 1");
        Assert.Single(trackingRows);
        Assert.Equal(0, Convert.ToInt32(trackingRows[0]["sync_row_is_tombstone"]));
    }

    [Fact]
    public async Task ProvisionTracking_AfterUpdate_UpdatesTrackingTimestamp()
    {
        // Arrange
        _db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL)");
        await _trackingManager.ProvisionTrackingAsync(_db, "users");
        _db.ExecuteSQL("INSERT INTO users (id, name) VALUES (1, 'Alice')");

        var initialTracking = _db.ExecuteQuery("SELECT timestamp FROM users_tracking WHERE id = 1");
        var initialTimestamp = Convert.ToInt64(initialTracking[0]["timestamp"]);

        // Wait to ensure timestamp changes
        await Task.Delay(10);

        // Act
        _db.ExecuteSQL("UPDATE users SET name = 'Bob' WHERE id = 1");

        // Assert
        var updatedTracking = _db.ExecuteQuery("SELECT timestamp, sync_row_is_tombstone FROM users_tracking WHERE id = 1");
        var updatedTimestamp = Convert.ToInt64(updatedTracking[0]["timestamp"]);

        Assert.True(updatedTimestamp > initialTimestamp);
        Assert.Equal(0, Convert.ToInt32(updatedTracking[0]["sync_row_is_tombstone"]));
    }

    [Fact]
    public async Task ProvisionTracking_AfterDelete_MarksTombstone()
    {
        // Arrange
        _db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL)");
        await _trackingManager.ProvisionTrackingAsync(_db, "users");
        _db.ExecuteSQL("INSERT INTO users (id, name) VALUES (1, 'Alice')");

        // Act
        _db.ExecuteSQL("DELETE FROM users WHERE id = 1");

        // Assert
        var trackingRows = _db.ExecuteQuery("SELECT sync_row_is_tombstone FROM users_tracking WHERE id = 1");
        Assert.Single(trackingRows);
        Assert.Equal(1, Convert.ToInt32(trackingRows[0]["sync_row_is_tombstone"]));
    }

    [Fact]
    public async Task DeprovisionTrackingAsync_RemovesTrackingTableAndTriggers()
    {
        // Arrange
        _db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL)");
        await _trackingManager.ProvisionTrackingAsync(_db, "users");

        // Act
        await _trackingManager.DeprovisionTrackingAsync(_db, "users");

        // Assert
        var tables = _db.GetTables();
        Assert.DoesNotContain(tables, t => t.Name == "users_tracking");

        var triggers = _db.ExecuteQuery("SELECT name FROM sqlite_master WHERE type='trigger' AND name LIKE 'trg_users_%'");
        Assert.Empty(triggers);
    }

    [Fact]
    public async Task IsProvisionedAsync_ReturnsTrueWhenProvisioned()
    {
        // Arrange
        _db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL)");
        await _trackingManager.ProvisionTrackingAsync(_db, "users");

        // Act
        var isProvisioned = await _trackingManager.IsProvisionedAsync(_db, "users");

        // Assert
        Assert.True(isProvisioned);
    }

    [Fact]
    public async Task IsProvisionedAsync_ReturnsFalseWhenNotProvisioned()
    {
        // Arrange
        _db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL)");

        // Act
        var isProvisioned = await _trackingManager.IsProvisionedAsync(_db, "users");

        // Assert
        Assert.False(isProvisioned);
    }

    [Fact]
    public async Task ProvisionTracking_WithoutPrimaryKey_ThrowsException()
    {
        // Arrange
        _db.ExecuteSQL("CREATE TABLE logs (message TEXT)");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _trackingManager.ProvisionTrackingAsync(_db, "logs"));
    }

    [Fact]
    public async Task ProvisionTracking_ForNonExistentTable_ThrowsException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _trackingManager.ProvisionTrackingAsync(_db, "nonexistent"));
    }
}
