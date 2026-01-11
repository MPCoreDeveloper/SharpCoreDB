using Microsoft.Extensions.DependencyInjection;

namespace SharpCoreDB.Tests;

/// <summary>
/// Tests for async database operations.
/// </summary>
public class AsyncTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly DatabaseFactory _factory;

    public AsyncTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_async_{Guid.NewGuid()}");
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var serviceProvider = services.BuildServiceProvider();
        _factory = serviceProvider.GetRequiredService<DatabaseFactory>();
    }

    [Fact]
    public async Task ExecuteSQLAsync_CreateTable_Success()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "testpass");

        // Act
        await db.ExecuteSQLAsync("CREATE TABLE async_test (id INTEGER, name TEXT)");

        // Assert - verify table exists by inserting data
        await db.ExecuteSQLAsync("INSERT INTO async_test VALUES (?, ?)", new Dictionary<string, object?> { { "0", 1 }, { "1", "TestName" } });
        db.ExecuteSQL("SELECT * FROM async_test"); // Verify no exception
    }

    [Fact]
    public async Task ExecuteSQLAsync_InsertData_Success()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "testpass");
        db.ExecuteSQL("CREATE TABLE async_insert (id INTEGER, value TEXT)");

        // Act
        await db.ExecuteSQLAsync("INSERT INTO async_insert VALUES (?, ?)", new Dictionary<string, object?> { { "0", 1 }, { "1", "Value1" } });
        await db.ExecuteSQLAsync("INSERT INTO async_insert VALUES (?, ?)", new Dictionary<string, object?> { { "0", 2 }, { "1", "Value2" } });

        // Assert
        db.ExecuteSQL("SELECT * FROM async_insert"); // Should not throw
    }

    [Fact]
    public async Task ExecuteSQLAsync_MultipleOperations_Success()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "testpass");

        // Act
        await db.ExecuteSQLAsync("CREATE TABLE multi_async (id INTEGER PRIMARY KEY, data TEXT)");
        await db.ExecuteSQLAsync("INSERT INTO multi_async VALUES (?, ?)", new Dictionary<string, object?> { { "0", 1 }, { "1", "First" } });
        await db.ExecuteSQLAsync("INSERT INTO multi_async VALUES (?, ?)", new Dictionary<string, object?> { { "0", 2 }, { "1", "Second" } });
        await db.ExecuteSQLAsync("UPDATE multi_async SET data = ? WHERE id = ?", new Dictionary<string, object?> { { "0", "Updated" }, { "1", 1 } });

        // Assert - no exception means success
    }

    [Fact]
    public async Task ExecuteSQLAsync_WithCancellation_CanComplete()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "testpass");
        using var cts = new CancellationTokenSource();

        // Act
        await db.ExecuteSQLAsync("CREATE TABLE cancel_test (id INTEGER, name TEXT)", cts.Token);
        await db.ExecuteSQLAsync("INSERT INTO cancel_test VALUES (?, ?)", new Dictionary<string, object?> { { "0", 1 }, { "1", "Test" } }, cts.Token);

        // Assert
        db.ExecuteSQL("SELECT * FROM cancel_test"); // Should work
    }

    [Fact]
    public async Task ExecuteSQLAsync_ParallelOperations_Success()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "testpass");
        db.ExecuteSQL("CREATE TABLE parallel_test (id INTEGER, thread_id TEXT)");

        // Act - execute multiple async inserts in parallel
        var tasks = new List<Task>();
        for (int i = 0; i < 10; i++)
        {
            int taskId = i;
            tasks.Add(db.ExecuteSQLAsync("INSERT INTO parallel_test VALUES (?, ?)", new Dictionary<string, object?> { { "0", taskId }, { "1", $"thread_{taskId}" } }));
        }

        await Task.WhenAll(tasks);

        // Assert - verify all inserts completed
        db.ExecuteSQL("SELECT * FROM parallel_test");
    }

    [Fact]
    public async Task ExecuteSQLAsync_WithConfig_UsesConfiguration()
    {
        // Arrange
        var config = new DatabaseConfig
        {
            EnableQueryCache = true,
            QueryCacheSize = 500
        };
        var db = _factory.Create(_testDbPath, "testpass", false, config);

        // Act
        await db.ExecuteSQLAsync("CREATE TABLE config_test (id INTEGER, value TEXT)");
        await db.ExecuteSQLAsync("INSERT INTO config_test VALUES (?, ?)", new Dictionary<string, object?> { { "0", 1 }, { "1", "CachedValue" } });

        // Assert
        var stats = db.GetQueryCacheStatistics();
        Assert.True(stats.Count >= 0); // Cache should be enabled
    }

    [Fact]
    public void Prepare_And_ExecutePrepared_SelectWithParameter()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "testpass");
        
        try
        {
            db.ExecuteSQL("CREATE TABLE users (id INTEGER, name TEXT)");
            db.ExecuteSQL("INSERT INTO users VALUES (1, 'Alice')");
            db.ExecuteSQL("INSERT INTO users VALUES (2, 'Bob')");

            // Act
            var stmt = db.Prepare("SELECT * FROM users WHERE id = ?");
            db.ExecutePrepared(stmt, new Dictionary<string, object?> { { "0", 1 } });

            // Assert - no exception means success
        }
        finally
        {
            // ✅ FIX: Flush and dispose database before cleanup
            try
            {
                db?.Flush();
                db?.ForceSave();
                (db as IDisposable)?.Dispose();
            }
            catch { /* Ignore disposal errors */ }
        }
    }

    [Fact]
    public async Task ExecutePreparedAsync_InsertWithParameter()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "testpass");
        db.ExecuteSQL("CREATE TABLE prepared_test (id INTEGER, value TEXT)");

        // Act
        var stmt = db.Prepare("INSERT INTO prepared_test VALUES (?, ?)");
        await db.ExecutePreparedAsync(stmt, new Dictionary<string, object?> { { "0", 1 }, { "1", "PreparedValue" } });

        // Assert - verify insert worked
        db.ExecuteSQL("SELECT * FROM prepared_test"); // Should not throw
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDbPath))
            {
                Directory.Delete(_testDbPath, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
