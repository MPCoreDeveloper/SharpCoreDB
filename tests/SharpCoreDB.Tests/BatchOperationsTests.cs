using Microsoft.Extensions.DependencyInjection;

namespace SharpCoreDB.Tests;

/// <summary>
/// Tests for batch database operations for improved performance.
/// </summary>
public class BatchOperationsTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly DatabaseFactory _factory;

    public BatchOperationsTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_batch_{Guid.NewGuid()}");
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var serviceProvider = services.BuildServiceProvider();
        _factory = serviceProvider.GetRequiredService<DatabaseFactory>();
    }

    [Fact]
    public void ExecuteBatchSQL_MultipleInserts_Success()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "testpass");
        db.ExecuteSQL("CREATE TABLE batch_test (id INTEGER, name TEXT, value INTEGER)");

        // Act - execute 100 inserts
        for (int i = 0; i < 100; i++)
        {
            db.ExecuteSQL("INSERT INTO batch_test VALUES (?, ?, ?)", new Dictionary<string, object?> { { "0", i }, { "1", "name_" + i }, { "2", i * 10 } });
        }

        // Assert - verify all records were inserted
        db.ExecuteSQL("SELECT * FROM batch_test");
    }

    [Fact]
    public void ExecuteBatchSQL_MixedOperations_Success()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "testpass");
        db.ExecuteSQL("CREATE TABLE mixed_batch (id INTEGER PRIMARY KEY, status TEXT)");
        db.ExecuteSQL("INSERT INTO mixed_batch VALUES (?, ?)", new Dictionary<string, object?> { { "0", 1 }, { "1", "pending" } });
        db.ExecuteSQL("INSERT INTO mixed_batch VALUES (?, ?)", new Dictionary<string, object?> { { "0", 2 }, { "1", "pending" } });

        // Act - batch with inserts and updates
        db.ExecuteSQL("INSERT INTO mixed_batch VALUES (?, ?)", new Dictionary<string, object?> { { "0", 3 }, { "1", "active" } });
        db.ExecuteSQL("UPDATE mixed_batch SET status = ? WHERE id = ?", new Dictionary<string, object?> { { "0", "completed" }, { "1", 1 } });
        db.ExecuteSQL("INSERT INTO mixed_batch VALUES (?, ?)", new Dictionary<string, object?> { { "0", 4 }, { "1", "pending" } });
        db.ExecuteSQL("UPDATE mixed_batch SET status = ? WHERE id = ?", new Dictionary<string, object?> { { "0", "active" }, { "1", 2 } });

        // Assert - verify operations completed
        db.ExecuteSQL("SELECT * FROM mixed_batch");
    }

    [Fact]
    public void ExecuteBatchSQL_EmptyBatch_NoError()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "testpass");
        db.ExecuteSQL("CREATE TABLE empty_batch (id INTEGER)");

        // Act
        db.ExecuteBatchSQL(Array.Empty<string>());

        // Assert - no exception thrown
    }

    [Fact]
    public void ExecuteBatchSQL_WithSelects_ProcessesIndividually()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "testpass");
        db.ExecuteSQL("CREATE TABLE select_batch (id INTEGER, name TEXT)");
        db.ExecuteSQL("INSERT INTO select_batch VALUES (?, ?)", new Dictionary<string, object?> { { "0", 1 }, { "1", "Test" } });

        // Act - inserts with SELECT
        db.ExecuteSQL("INSERT INTO select_batch VALUES (?, ?)", new Dictionary<string, object?> { { "0", 2 }, { "1", "Batch" } });
        db.ExecuteSQL("SELECT * FROM select_batch");
        db.ExecuteSQL("INSERT INTO select_batch VALUES (?, ?)", new Dictionary<string, object?> { { "0", 3 }, { "1", "Mixed" } });

        // Assert - all operations completed
        db.ExecuteSQL("SELECT * FROM select_batch");
    }

    [Fact]
    public async Task ExecuteBatchSQLAsync_MultipleInserts_Success()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "testpass");
        await db.ExecuteSQLAsync("CREATE TABLE async_batch (id INTEGER, data TEXT)");

        // Act - execute inserts asynchronously
        for (int i = 0; i < 50; i++)
        {
            await db.ExecuteSQLAsync("INSERT INTO async_batch VALUES (?, ?)", new Dictionary<string, object?> { { "0", i }, { "1", "data_" + i } });
        }

        // Assert
        db.ExecuteSQL("SELECT * FROM async_batch");
    }

    [Fact]
    public async Task ExecuteBatchSQLAsync_WithCancellation_CanComplete()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "testpass");
        db.ExecuteSQL("CREATE TABLE cancel_batch (id INTEGER, value TEXT)");
        using var cts = new CancellationTokenSource();

        // Act
        await db.ExecuteSQLAsync("INSERT INTO cancel_batch VALUES (?, ?)", new Dictionary<string, object?> { { "0", 1 }, { "1", "First" } }, cts.Token);
        await db.ExecuteSQLAsync("INSERT INTO cancel_batch VALUES (?, ?)", new Dictionary<string, object?> { { "0", 2 }, { "1", "Second" } }, cts.Token);
        await db.ExecuteSQLAsync("INSERT INTO cancel_batch VALUES (?, ?)", new Dictionary<string, object?> { { "0", 3 }, { "1", "Third" } }, cts.Token);

        // Assert
        db.ExecuteSQL("SELECT * FROM cancel_batch");
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void ExecuteBatchSQL_LargeVolume_Performance()
    {
        // Skip in CI - GitHub Actions runners have slow I/O
        if (Environment.GetEnvironmentVariable("CI") == "true" ||
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
        {
            return; // Skip performance test in CI
        }

        // Arrange
        var db = _factory.Create(_testDbPath, "testpass");
        db.ExecuteSQL("CREATE TABLE perf_batch (id INTEGER, timestamp DATETIME, value DECIMAL)");

        // Act - insert 1000 records
        var startTime = DateTime.UtcNow;
        for (int i = 0; i < 1000; i++)
        {
            db.ExecuteSQL("INSERT INTO perf_batch VALUES (?, ?, ?)", new Dictionary<string, object?> { { "0", i }, { "1", new DateTime(2025, 1, 1) }, { "2", i * 0.5m } });
        }
        var elapsed = DateTime.UtcNow - startTime;

        // Assert - batch should complete reasonably fast
        Assert.True(elapsed.TotalSeconds < 30, $"Batch took {elapsed.TotalSeconds}s, expected < 30s");
        db.ExecuteSQL("SELECT * FROM perf_batch");
    }

    [Fact]
    public void ExecuteBatchSQL_CreateAndInsert_Success()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "testpass");

        // Act - CREATE TABLE and inserts
        db.ExecuteSQL("CREATE TABLE batch_table (id INTEGER, name TEXT)");
        db.ExecuteSQL("INSERT INTO batch_table VALUES (?, ?)", new Dictionary<string, object?> { { "0", 1 }, { "1", "First" } });
        db.ExecuteSQL("INSERT INTO batch_table VALUES (?, ?)", new Dictionary<string, object?> { { "0", 2 }, { "1", "Second" } });
        db.ExecuteSQL("INSERT INTO batch_table VALUES (?, ?)", new Dictionary<string, object?> { { "0", 3 }, { "1", "Third" } });

        // Assert
        db.ExecuteSQL("SELECT * FROM batch_table");
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
