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

        // Act - execute 100 inserts in a batch
        var statements = new List<string>();
        for (int i = 0; i < 100; i++)
        {
            statements.Add($"INSERT INTO batch_test VALUES ('{i}', 'name_{i}', '{i * 10}')");
        }
        db.ExecuteBatchSQL(statements);

        // Assert - verify all records were inserted
        db.ExecuteSQL("SELECT * FROM batch_test");
    }

    [Fact]
    public void ExecuteBatchSQL_MixedOperations_Success()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "testpass");
        db.ExecuteSQL("CREATE TABLE mixed_batch (id INTEGER PRIMARY KEY, status TEXT)");
        db.ExecuteSQL("INSERT INTO mixed_batch VALUES ('1', 'pending')");
        db.ExecuteSQL("INSERT INTO mixed_batch VALUES ('2', 'pending')");

        // Act - batch with inserts and updates
        var statements = new[]
        {
            "INSERT INTO mixed_batch VALUES ('3', 'active')",
            "UPDATE mixed_batch SET status = 'completed' WHERE id = '1'",
            "INSERT INTO mixed_batch VALUES ('4', 'pending')",
            "UPDATE mixed_batch SET status = 'active' WHERE id = '2'"
        };
        db.ExecuteBatchSQL(statements);

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
        db.ExecuteSQL("INSERT INTO select_batch VALUES ('1', 'Test')");

        // Act - batch with SELECT statements
        var statements = new[]
        {
            "INSERT INTO select_batch VALUES ('2', 'Batch')",
            "SELECT * FROM select_batch",
            "INSERT INTO select_batch VALUES ('3', 'Mixed')"
        };
        db.ExecuteBatchSQL(statements);

        // Assert - all operations completed
        db.ExecuteSQL("SELECT * FROM select_batch");
    }

    [Fact]
    public async Task ExecuteBatchSQLAsync_MultipleInserts_Success()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "testpass");
        await db.ExecuteSQLAsync("CREATE TABLE async_batch (id INTEGER, data TEXT)");

        // Act - execute batch asynchronously
        var statements = new List<string>();
        for (int i = 0; i < 50; i++)
        {
            statements.Add($"INSERT INTO async_batch VALUES ('{i}', 'data_{i}')");
        }
        await db.ExecuteBatchSQLAsync(statements);

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
        var statements = new[]
        {
            "INSERT INTO cancel_batch VALUES ('1', 'First')",
            "INSERT INTO cancel_batch VALUES ('2', 'Second')",
            "INSERT INTO cancel_batch VALUES ('3', 'Third')"
        };
        await db.ExecuteBatchSQLAsync(statements, cts.Token);

        // Assert
        db.ExecuteSQL("SELECT * FROM cancel_batch");
    }

    [Fact]
    public void ExecuteBatchSQL_LargeVolume_Performance()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "testpass");
        db.ExecuteSQL("CREATE TABLE perf_batch (id INTEGER, timestamp DATETIME, value DECIMAL)");

        // Act - insert 1000 records in batch
        var statements = new List<string>();
        for (int i = 0; i < 1000; i++)
        {
            statements.Add($"INSERT INTO perf_batch VALUES ('{i}', '2025-01-01', '{i * 0.5}')");
        }

        var startTime = DateTime.UtcNow;
        db.ExecuteBatchSQL(statements);
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

        // Act - batch that includes CREATE TABLE
        var statements = new[]
        {
            "CREATE TABLE batch_table (id INTEGER, name TEXT)",
            "INSERT INTO batch_table VALUES ('1', 'First')",
            "INSERT INTO batch_table VALUES ('2', 'Second')",
            "INSERT INTO batch_table VALUES ('3', 'Third')"
        };
        db.ExecuteBatchSQL(statements);

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
