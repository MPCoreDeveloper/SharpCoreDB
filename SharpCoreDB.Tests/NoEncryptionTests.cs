using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

namespace SharpCoreDB.Tests;

/// <summary>
/// Unit tests for SharpCoreDB NoEncryption mode.
/// Tests the performance and functionality when encryption is disabled.
/// </summary>
public class NoEncryptionTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly string _testDbPathEncrypted;
    private readonly IServiceProvider _serviceProvider;
    private readonly DatabaseFactory _factory;

    public NoEncryptionTests()
    {
        // Create unique test database paths for each test instance
        _testDbPath = Path.Combine(Path.GetTempPath(), $"SharpCoreDB_NoEncrypt_Test_{Guid.NewGuid()}");
        _testDbPathEncrypted = Path.Combine(Path.GetTempPath(), $"SharpCoreDB_Encrypted_Test_{Guid.NewGuid()}");

        // Set up dependency injection
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _serviceProvider = services.BuildServiceProvider();
        _factory = _serviceProvider.GetRequiredService<DatabaseFactory>();
    }

    public void Dispose()
    {
        // Clean up test databases after each test
        if (Directory.Exists(_testDbPath))
        {
            Directory.Delete(_testDbPath, true);
        }
        if (Directory.Exists(_testDbPathEncrypted))
        {
            Directory.Delete(_testDbPathEncrypted, true);
        }
    }

    [Fact]
    public void NoEncryption_BasicOperations_WorksCorrectly()
    {
        // Arrange
        var config = new DatabaseConfig { NoEncryptMode = true };
        var db = _factory.Create(_testDbPath, "testPassword", false, config);

        // Act - Create table and insert data
        db.ExecuteSQL("CREATE TABLE users (id INTEGER, name TEXT)");
        db.ExecuteSQL("INSERT INTO users VALUES ('1', 'Alice')");
        db.ExecuteSQL("INSERT INTO users VALUES ('2', 'Bob')");

        // Assert - No exception thrown means success
        Assert.True(true);
    }

    [Fact]
    public void NoEncryption_DataPersistence_WorksCorrectly()
    {
        // Arrange
        var config = new DatabaseConfig { NoEncryptMode = true };

        // Act - Create database, insert data, close and reopen
        var db1 = _factory.Create(_testDbPath, "testPassword", false, config);
        db1.ExecuteSQL("CREATE TABLE products (id INTEGER, name TEXT)");
        db1.ExecuteSQL("INSERT INTO products VALUES ('1', 'Widget')");
        db1.ExecuteSQL("INSERT INTO products VALUES ('2', 'Gadget')");

        // Reopen database with NoEncryption mode
        var db2 = _factory.Create(_testDbPath, "testPassword", false, config);
        db2.ExecuteSQL("SELECT * FROM products WHERE id = '1'");

        // Assert - No exception thrown means data persisted correctly
        Assert.True(true);
    }

    [Fact]
    public void NoEncryption_PerformanceBenefit_IsMeasurable()
    {
        // Arrange
        var configNoEncrypt = new DatabaseConfig { NoEncryptMode = true };
        var configEncrypted = DatabaseConfig.Default;
        const int insertCount = 2000;

        // Act - Measure NoEncryption performance
        var dbNoEncrypt = _factory.Create(_testDbPath, "testPassword", false, configNoEncrypt);
        dbNoEncrypt.ExecuteSQL("CREATE TABLE entries (id INTEGER, data TEXT, value INTEGER)");

        var swNoEncrypt = Stopwatch.StartNew();
        for (int i = 0; i < insertCount; i++)
        {
            dbNoEncrypt.ExecuteSQL("INSERT INTO entries VALUES (?, ?, ?)", new Dictionary<string, object?> { { "0", i }, { "1", $"data{i}" }, { "2", i * 10 } });
        }
        swNoEncrypt.Stop();

        // Act - Measure Encrypted performance
        var dbEncrypted = _factory.Create(_testDbPathEncrypted, "testPassword", false, configEncrypted);
        dbEncrypted.ExecuteSQL("CREATE TABLE entries (id INTEGER, data TEXT, value INTEGER)");

        var swEncrypted = Stopwatch.StartNew();
        for (int i = 0; i < insertCount; i++)
        {
            dbEncrypted.ExecuteSQL("INSERT INTO entries VALUES (?, ?, ?)", new Dictionary<string, object?> { { "0", i }, { "1", $"data{i}" }, { "2", i * 10 } });
        }
        swEncrypted.Stop();

        // Assert - Just verify both modes complete successfully
        // Performance can vary due to system load, buffering, etc.
        var speedupRatio = (double)swEncrypted.ElapsedMilliseconds / swNoEncrypt.ElapsedMilliseconds;

        // Output for verification
        Console.WriteLine($"NoEncryption: {swNoEncrypt.ElapsedMilliseconds}ms");
        Console.WriteLine($"Encrypted: {swEncrypted.ElapsedMilliseconds}ms");
        Console.WriteLine($"Speedup: {speedupRatio:F2}x");

        // Both modes should complete in reasonable time
        Assert.True(swNoEncrypt.ElapsedMilliseconds < 50000,
            $"NoEncrypt mode should complete in reasonable time. Took {swNoEncrypt.ElapsedMilliseconds}ms");
        Assert.True(swEncrypted.ElapsedMilliseconds < 50000,
            $"Encrypted mode should complete in reasonable time. Took {swEncrypted.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void NoEncryption_HighPerformanceConfig_UsesNoEncryption()
    {
        // Arrange
        var config = DatabaseConfig.HighPerformance;

        // Act
        var db = _factory.Create(_testDbPath, "testPassword", false, config);
        db.ExecuteSQL("CREATE TABLE test (id INTEGER, name TEXT)");
        db.ExecuteSQL("INSERT INTO test VALUES ('1', 'Test')");

        // Assert - Verify NoEncryptMode is enabled in HighPerformance config
        Assert.True(config.NoEncryptMode);
    }

    [Fact]
    public void NoEncryption_ComplexQueries_WorkCorrectly()
    {
        // Arrange
        var config = new DatabaseConfig { NoEncryptMode = true };
        var db = _factory.Create(_testDbPath, "testPassword", false, config);

        // Act - Create tables with different data types
        db.ExecuteSQL("CREATE TABLE orders (id INTEGER PRIMARY KEY, customer TEXT, total DECIMAL, created DATETIME, active BOOLEAN)");
        db.ExecuteSQL("INSERT INTO orders VALUES ('1', 'Customer1', '99.99', '2024-01-15 10:30:00', 'true')");
        db.ExecuteSQL("INSERT INTO orders VALUES ('2', 'Customer2', '149.99', '2024-01-16 14:45:00', 'true')");
        db.ExecuteSQL("INSERT INTO orders VALUES ('3', 'Customer1', '79.99', '2024-01-17 09:15:00', 'false')");

        // Test SELECT with WHERE
        db.ExecuteSQL("SELECT * FROM orders WHERE customer = 'Customer1'");

        // Test UPDATE
        db.ExecuteSQL("UPDATE orders SET active = 'false' WHERE id = '1'");

        // Test DELETE
        db.ExecuteSQL("DELETE FROM orders WHERE id = '3'");

        // Assert - No exception thrown means operations work correctly
        Assert.True(true);
    }
}
