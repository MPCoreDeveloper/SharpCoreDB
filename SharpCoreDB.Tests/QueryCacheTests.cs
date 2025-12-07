using Microsoft.Extensions.DependencyInjection;

namespace SharpCoreDB.Tests;

/// <summary>
/// Tests for query caching functionality.
/// </summary>
public class QueryCacheTests
{
    private readonly string _testDbPath;
    private readonly IServiceProvider _serviceProvider;

    public QueryCacheTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_query_cache_{Guid.NewGuid()}");
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public void QueryCache_RepeatedQueries_ImprovesCacheHitRate()
    {
        // Arrange
        var factory = _serviceProvider.GetRequiredService<DatabaseFactory>();
        var config = new DatabaseConfig { EnableQueryCache = true, QueryCacheSize = 100 };
        var db = factory.Create(_testDbPath, "test123", false, config);

        db.ExecuteSQL("CREATE TABLE users (id INTEGER, name TEXT)");
        db.ExecuteSQL("INSERT INTO users VALUES ('1', 'Alice')");
        db.ExecuteSQL("INSERT INTO users VALUES ('2', 'Bob')");

        // Act - Execute same SELECT query multiple times
        for (int i = 0; i < 10; i++)
        {
            db.ExecuteSQL("SELECT * FROM users WHERE id = '1'");
        }

        // Assert
        var stats = db.GetQueryCacheStatistics();
        Assert.True(stats.Hits > 0, "Cache should have hits from repeated queries");
        // Note: CREATE TABLE and INSERT queries also get cached, so hit rate will be lower
        // With 1 CREATE + 2 INSERTs + 10 SELECTs (9 repeated) = 13 total queries
        // Only SELECT repeats contribute to hits, so 9 hits out of 13 = 69%
        Assert.True(stats.HitRate > 0.6, $"Hit rate should be >60%, got {stats.HitRate:P2}");

        // Cleanup
        Directory.Delete(_testDbPath, true);
    }

    [Fact]
    public void QueryCache_GroupByQueries_CachesEffectively()
    {
        // Arrange
        var factory = _serviceProvider.GetRequiredService<DatabaseFactory>();
        var config = new DatabaseConfig { EnableQueryCache = true, QueryCacheSize = 100 };
        var db = factory.Create(_testDbPath, "test123", false, config);

        db.ExecuteSQL("CREATE TABLE time_entries (id INTEGER, project TEXT, duration INTEGER)");
        db.ExecuteSQL("INSERT INTO time_entries VALUES ('1', 'Alpha', '60')");
        db.ExecuteSQL("INSERT INTO time_entries VALUES ('2', 'Beta', '90')");
        db.ExecuteSQL("INSERT INTO time_entries VALUES ('3', 'Alpha', '30')");

        // Act - Execute same GROUP BY query multiple times (common for reports)
        var query = "SELECT project, SUM(duration) FROM time_entries GROUP BY project";
        for (int i = 0; i < 20; i++)
        {
            db.ExecuteSQL(query);
        }

        // Assert
        var stats = db.GetQueryCacheStatistics();
        Assert.True(stats.Hits >= 19, $"Should have at least 19 cache hits, got {stats.Hits}");
        // With 1 CREATE + 3 INSERTs + 20 SELECTs (19 repeated) = 24 total queries
        // So hit rate should be 19/24 = 79%
        Assert.True(stats.HitRate > 0.75, $"Hit rate should be >75% for repeated reports, got {stats.HitRate:P2}");

        // Cleanup
        Directory.Delete(_testDbPath, true);
    }

    [Fact]
    public void QueryCache_Disabled_HasNoHits()
    {
        // Arrange
        var factory = _serviceProvider.GetRequiredService<DatabaseFactory>();
        var config = new DatabaseConfig { EnableQueryCache = false };
        var db = factory.Create(_testDbPath, "test123", false, config);

        db.ExecuteSQL("CREATE TABLE users (id INTEGER, name TEXT)");
        db.ExecuteSQL("INSERT INTO users VALUES ('1', 'Alice')");

        // Act
        for (int i = 0; i < 5; i++)
        {
            db.ExecuteSQL("SELECT * FROM users");
        }

        // Assert
        var stats = db.GetQueryCacheStatistics();
        Assert.Equal(0, stats.Hits);
        Assert.Equal(0, stats.Misses);
        Assert.Equal(0, stats.Count);

        // Cleanup
        Directory.Delete(_testDbPath, true);
    }

    [Fact]
    public void QueryCache_DifferentQueries_CachesEachQuery()
    {
        // Arrange
        var factory = _serviceProvider.GetRequiredService<DatabaseFactory>();
        var config = new DatabaseConfig { EnableQueryCache = true, QueryCacheSize = 100 };
        var db = factory.Create(_testDbPath, "test123", false, config);

        db.ExecuteSQL("CREATE TABLE users (id INTEGER, name TEXT)");
        db.ExecuteSQL("INSERT INTO users VALUES ('1', 'Alice')");
        db.ExecuteSQL("INSERT INTO users VALUES ('2', 'Bob')");

        // Act - Execute different queries
        db.ExecuteSQL("SELECT * FROM users WHERE id = '1'");
        db.ExecuteSQL("SELECT * FROM users WHERE id = '2'");
        db.ExecuteSQL("SELECT * FROM users");

        // Execute them again
        db.ExecuteSQL("SELECT * FROM users WHERE id = '1'");
        db.ExecuteSQL("SELECT * FROM users WHERE id = '2'");
        db.ExecuteSQL("SELECT * FROM users");

        // Assert
        var stats = db.GetQueryCacheStatistics();
        Assert.Equal(3, stats.Hits); // Second execution of each SELECT query should be a hit
        // All queries get cached: CREATE + 2 INSERTs + 3 unique SELECTs = 6 misses initially
        // Then 3 repeated SELECTs = 3 hits
        Assert.Equal(6, stats.Misses);
        Assert.True(stats.Count >= 6, $"Should have cached at least 6 different queries, got {stats.Count}");

        // Cleanup
        Directory.Delete(_testDbPath, true);
    }

    [Fact]
    public void QueryCache_CacheSizeLimit_EvictsLeastUsed()
    {
        // Arrange
        var factory = _serviceProvider.GetRequiredService<DatabaseFactory>();
        var config = new DatabaseConfig { EnableQueryCache = true, QueryCacheSize = 10 }; // Small cache
        var db = factory.Create(_testDbPath, "test123", false, config);

        db.ExecuteSQL("CREATE TABLE users (id INTEGER, name TEXT)");

        // Act - Execute many different queries to exceed cache size
        for (int i = 0; i < 20; i++)
        {
            db.ExecuteSQL($"INSERT INTO users VALUES ('{i}', 'User{i}')");
        }

        var stats = db.GetQueryCacheStatistics();

        // Assert - Cache should not grow beyond limit
        Assert.True(stats.Count <= config.QueryCacheSize,
            $"Cache size should be <= {config.QueryCacheSize}, got {stats.Count}");

        // Cleanup
        Directory.Delete(_testDbPath, true);
    }

    [Fact]
    public void QueryCache_ParameterizedQueries_ImprovesHitRate()
    {
        // Arrange
        var factory = _serviceProvider.GetRequiredService<DatabaseFactory>();
        var config = new DatabaseConfig { EnableQueryCache = true, QueryCacheSize = 1024 };
        var db = factory.Create(_testDbPath, "test123", false, config);

        db.ExecuteSQL("CREATE TABLE users (id INTEGER, name TEXT)");
        for (int i = 0; i < 100; i++)
        {
            db.ExecuteSQL($"INSERT INTO users VALUES ({i}, 'User{i}')");
        }

        // Act - Execute same parameterized SELECT query multiple times with different @id
        var sql = "SELECT * FROM users WHERE id = ?";
        for (int i = 0; i < 50; i++)
        {
            db.ExecuteSQL(sql, new Dictionary<string, object?> { ["0"] = i % 100 });
        }

        // Assert
        var stats = db.GetQueryCacheStatistics();
        Assert.True(stats.Hits > 0, "Cache should have hits from repeated parameterized queries");
        // With 1 CREATE + 100 INSERTs + 50 SELECTs (49 repeated) = 151 total queries
        // Cache should improve hit rate for repeated parameterized queries
        Assert.True(stats.HitRate > 0.3, $"Hit rate should be >30% for repeated parameterized queries, got {stats.HitRate:P2}");

        // Cleanup
        Directory.Delete(_testDbPath, true);
    }

    [Fact]
    public void QueryCache_1000RepeatedQueries_HighHitRate()
    {
        // Arrange
        var factory = _serviceProvider.GetRequiredService<DatabaseFactory>();
        var config = new DatabaseConfig { EnableQueryCache = true, QueryCacheSize = 1024 };
        var db = factory.Create(_testDbPath, "test123", false, config);

        db.ExecuteSQL("CREATE TABLE test_data (id INTEGER, value TEXT)");
        for (int i = 0; i < 100; i++)
        {
            db.ExecuteSQL($"INSERT INTO test_data VALUES ({i}, 'Value{i}')");
        }

        // Act - Execute the same SELECT query 1000 times
        var sql = "SELECT * FROM test_data WHERE id = 50";
        for (int i = 0; i < 1000; i++)
        {
            db.ExecuteSQL(sql);
        }

        // Assert
        var stats = db.GetQueryCacheStatistics();
        Assert.True(stats.Hits >= 999, $"Should have at least 999 cache hits, got {stats.Hits}");
        Assert.True(stats.HitRate > 0.80, $"Hit rate should be >80% for 1000 repeated queries, got {stats.HitRate:P2}");

        // Cleanup
        Directory.Delete(_testDbPath, true);
    }
}
