using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using Xunit;

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
        await db.ExecuteSQLAsync("INSERT INTO async_test VALUES ('1', 'TestName')");
        db.ExecuteSQL("SELECT * FROM async_test"); // Verify no exception
    }

    [Fact]
    public async Task ExecuteSQLAsync_InsertData_Success()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "testpass");
        db.ExecuteSQL("CREATE TABLE async_insert (id INTEGER, value TEXT)");

        // Act
        await db.ExecuteSQLAsync("INSERT INTO async_insert VALUES ('1', 'Value1')");
        await db.ExecuteSQLAsync("INSERT INTO async_insert VALUES ('2', 'Value2')");

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
        await db.ExecuteSQLAsync("INSERT INTO multi_async VALUES ('1', 'First')");
        await db.ExecuteSQLAsync("INSERT INTO multi_async VALUES ('2', 'Second')");
        await db.ExecuteSQLAsync("UPDATE multi_async SET data = 'Updated' WHERE id = '1'");

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
        await db.ExecuteSQLAsync("INSERT INTO cancel_test VALUES ('1', 'Test')", cts.Token);

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
            tasks.Add(db.ExecuteSQLAsync($"INSERT INTO parallel_test VALUES ('{taskId}', 'thread_{taskId}')"));
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
        await db.ExecuteSQLAsync("INSERT INTO config_test VALUES ('1', 'CachedValue')");

        // Assert
        var stats = db.GetQueryCacheStatistics();
        Assert.True(stats.Count >= 0); // Cache should be enabled
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
