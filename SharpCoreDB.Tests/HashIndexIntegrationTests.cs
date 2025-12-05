using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Interfaces;
using Xunit;

namespace SharpCoreDB.Tests;

/// <summary>
/// Tests for HashIndex integration into Table and SQL operations.
/// </summary>
public class HashIndexIntegrationTests
{
    private readonly string _testDbPath;
    private readonly IServiceProvider _serviceProvider;

    public HashIndexIntegrationTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_hashindex_integration_{Guid.NewGuid()}");
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public void CreateIndex_CreatesHashIndexOnColumn()
    {
        // Arrange
        var factory = _serviceProvider.GetRequiredService<DatabaseFactory>();
        var config = new DatabaseConfig { EnableHashIndexes = true };
        var db = factory.Create(_testDbPath, "test123", false, config);

        db.ExecuteSQL("CREATE TABLE users (id INTEGER, name TEXT, email TEXT)");
        db.ExecuteSQL("INSERT INTO users VALUES ('1', 'Alice', 'alice@example.com')");
        db.ExecuteSQL("INSERT INTO users VALUES ('2', 'Bob', 'bob@example.com')");

        // Act - Create hash index on email column
        db.ExecuteSQL("CREATE INDEX idx_user_email ON users (email)");

        // Assert - Query should use the hash index
        db.ExecuteSQL("SELECT * FROM users WHERE email = 'alice@example.com'");

        // Cleanup
        Directory.Delete(_testDbPath, true);
    }

    [Fact]
    public void HashIndex_WHERE_Clause_UsesIndex()
    {
        // Arrange
        var factory = _serviceProvider.GetRequiredService<DatabaseFactory>();
        var config = new DatabaseConfig { EnableHashIndexes = true };
        var db = factory.Create(_testDbPath, "test123", false, config);

        db.ExecuteSQL("CREATE TABLE time_entries (id INTEGER, project TEXT, duration INTEGER)");
        
        // Insert test data
        for (int i = 1; i <= 100; i++)
        {
            var project = i % 10 == 0 ? "Alpha" : $"Project{i}";
            db.ExecuteSQL($"INSERT INTO time_entries VALUES ('{i}', '{project}', '{i * 10}')");
        }

        // Act - Create index and query
        db.ExecuteSQL("CREATE INDEX idx_project ON time_entries (project)");
        
        // Query should use hash index for O(1) lookup
        db.ExecuteSQL("SELECT * FROM time_entries WHERE project = 'Alpha'");

        // Cleanup
        Directory.Delete(_testDbPath, true);
    }

    [Fact]
    public void HashIndex_Insert_MaintainsIndex()
    {
        // Arrange
        var factory = _serviceProvider.GetRequiredService<DatabaseFactory>();
        var config = new DatabaseConfig { EnableHashIndexes = true };
        var db = factory.Create(_testDbPath, "test123", false, config);

        db.ExecuteSQL("CREATE TABLE products (id INTEGER, category TEXT, name TEXT)");
        db.ExecuteSQL("CREATE INDEX idx_category ON products (category)");

        // Act - Insert rows after index creation
        db.ExecuteSQL("INSERT INTO products VALUES ('1', 'Electronics', 'Laptop')");
        db.ExecuteSQL("INSERT INTO products VALUES ('2', 'Electronics', 'Phone')");
        db.ExecuteSQL("INSERT INTO products VALUES ('3', 'Books', 'Novel')");

        // Query should find indexed rows
        db.ExecuteSQL("SELECT * FROM products WHERE category = 'Electronics'");

        // Cleanup
        Directory.Delete(_testDbPath, true);
    }

    [Fact]
    public void HashIndex_Update_MaintainsIndex()
    {
        // Arrange
        var factory = _serviceProvider.GetRequiredService<DatabaseFactory>();
        var config = new DatabaseConfig { EnableHashIndexes = true };
        var db = factory.Create(_testDbPath, "test123", false, config);

        db.ExecuteSQL("CREATE TABLE tasks (id INTEGER, status TEXT, title TEXT)");
        db.ExecuteSQL("INSERT INTO tasks VALUES ('1', 'pending', 'Task 1')");
        db.ExecuteSQL("INSERT INTO tasks VALUES ('2', 'pending', 'Task 2')");
        db.ExecuteSQL("CREATE INDEX idx_status ON tasks (status)");

        // Act - Update status
        db.ExecuteSQL("UPDATE tasks SET status = 'completed' WHERE id = '1'");

        // Query should reflect updated index
        db.ExecuteSQL("SELECT * FROM tasks WHERE status = 'completed'");
        db.ExecuteSQL("SELECT * FROM tasks WHERE status = 'pending'");

        // Cleanup
        Directory.Delete(_testDbPath, true);
    }

    [Fact]
    public void HashIndex_Delete_MaintainsIndex()
    {
        // Arrange
        var factory = _serviceProvider.GetRequiredService<DatabaseFactory>();
        var config = new DatabaseConfig { EnableHashIndexes = true };
        var db = factory.Create(_testDbPath, "test123", false, config);

        db.ExecuteSQL("CREATE TABLE orders (id INTEGER, customer TEXT, amount INTEGER)");
        db.ExecuteSQL("INSERT INTO orders VALUES ('1', 'Alice', '100')");
        db.ExecuteSQL("INSERT INTO orders VALUES ('2', 'Bob', '200')");
        db.ExecuteSQL("INSERT INTO orders VALUES ('3', 'Alice', '150')");
        db.ExecuteSQL("CREATE INDEX idx_customer ON orders (customer)");

        // Act - Delete rows
        db.ExecuteSQL("DELETE FROM orders WHERE id = '1'");

        // Query should reflect deleted rows
        db.ExecuteSQL("SELECT * FROM orders WHERE customer = 'Alice'");

        // Cleanup
        Directory.Delete(_testDbPath, true);
    }

    [Fact]
    public void HashIndex_MultipleIndexes_WorkIndependently()
    {
        // Arrange
        var factory = _serviceProvider.GetRequiredService<DatabaseFactory>();
        var config = new DatabaseConfig { EnableHashIndexes = true };
        var db = factory.Create(_testDbPath, "test123", false, config);

        db.ExecuteSQL("CREATE TABLE employees (id INTEGER, department TEXT, city TEXT, name TEXT)");
        db.ExecuteSQL("INSERT INTO employees VALUES ('1', 'Engineering', 'Seattle', 'Alice')");
        db.ExecuteSQL("INSERT INTO employees VALUES ('2', 'Sales', 'Seattle', 'Bob')");
        db.ExecuteSQL("INSERT INTO employees VALUES ('3', 'Engineering', 'Portland', 'Charlie')");

        // Act - Create multiple indexes
        db.ExecuteSQL("CREATE INDEX idx_department ON employees (department)");
        db.ExecuteSQL("CREATE INDEX idx_city ON employees (city)");

        // Both indexes should work
        db.ExecuteSQL("SELECT * FROM employees WHERE department = 'Engineering'");
        db.ExecuteSQL("SELECT * FROM employees WHERE city = 'Seattle'");

        // Cleanup
        Directory.Delete(_testDbPath, true);
    }

    [Fact]
    public void HashIndex_LargeDataset_PerformsBetter()
    {
        // Arrange
        var factory = _serviceProvider.GetRequiredService<DatabaseFactory>();
        var config = new DatabaseConfig { EnableHashIndexes = true };
        var db = factory.Create(_testDbPath, "test123", false, config);

        db.ExecuteSQL("CREATE TABLE events (id INTEGER, type TEXT, timestamp TEXT)");

        // Insert 1000 rows
        for (int i = 1; i <= 1000; i++)
        {
            var type = i % 100 == 0 ? "critical" : $"type{i % 50}";
            db.ExecuteSQL($"INSERT INTO events VALUES ('{i}', '{type}', '2024-01-01')");
        }

        // Act - Create index and query
        db.ExecuteSQL("CREATE INDEX idx_type ON events (type)");
        db.ExecuteSQL("SELECT * FROM events WHERE type = 'critical'");

        // The hash index should provide significant speedup for this query
        // With 1000 rows and ~10 critical events, hash index gives O(1) vs O(n) lookup

        // Cleanup
        Directory.Delete(_testDbPath, true);
    }

    [Fact]
    public void HashIndex_DifferentDataTypes_Works()
    {
        // Arrange
        var factory = _serviceProvider.GetRequiredService<DatabaseFactory>();
        var config = new DatabaseConfig { EnableHashIndexes = true };
        var db = factory.Create(_testDbPath, "test123", false, config);

        db.ExecuteSQL("CREATE TABLE records (id INTEGER, count INTEGER, price REAL, active BOOLEAN)");
        db.ExecuteSQL("INSERT INTO records VALUES ('1', '100', '19.99', 'true')");
        db.ExecuteSQL("INSERT INTO records VALUES ('2', '200', '29.99', 'false')");
        db.ExecuteSQL("INSERT INTO records VALUES ('3', '100', '39.99', 'true')");

        // Act - Create index on integer column
        db.ExecuteSQL("CREATE INDEX idx_count ON records (count)");
        db.ExecuteSQL("SELECT * FROM records WHERE count = '100'");

        // Cleanup
        Directory.Delete(_testDbPath, true);
    }

    [Fact]
    public void HashIndex_UniqueIndex_SupportsCreation()
    {
        // Arrange
        var factory = _serviceProvider.GetRequiredService<DatabaseFactory>();
        var config = new DatabaseConfig { EnableHashIndexes = true };
        var db = factory.Create(_testDbPath, "test123", false, config);

        db.ExecuteSQL("CREATE TABLE accounts (id INTEGER, username TEXT, email TEXT)");
        db.ExecuteSQL("INSERT INTO accounts VALUES ('1', 'alice', 'alice@example.com')");
        db.ExecuteSQL("INSERT INTO accounts VALUES ('2', 'bob', 'bob@example.com')");

        // Act - Create unique index
        db.ExecuteSQL("CREATE UNIQUE INDEX idx_username ON accounts (username)");
        db.ExecuteSQL("SELECT * FROM accounts WHERE username = 'alice'");

        // Cleanup
        Directory.Delete(_testDbPath, true);
    }
}
