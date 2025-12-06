using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;

// This demo showcases the capabilities of SharpCoreDB, a lightweight, encrypted, file-based database
// that supports basic SQL operations like CREATE TABLE, INSERT, and SELECT with WHERE and ORDER BY clauses.
// SharpCoreDB uses JSON files for persistence, implements Write-Ahead Logging (WAL) for durability,
// and provides user authentication with encrypted storage.

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("SharpCoreDB Demo - A Lightweight Encrypted Database");
        Console.WriteLine("===============================================");

        // Clean up any previous demo data
        string dbPath = Path.Combine(Path.GetTempPath(), "SharpCoreDBDemo2");
        if (Directory.Exists(dbPath))
        {
            Directory.Delete(dbPath, true);
        }

        // Step 1: Set up Dependency Injection
        // SharpCoreDB uses dependency injection for its services. We use the AddSharpCoreDB extension
        // to register all required services, making the database self-contained.
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var serviceProvider = services.BuildServiceProvider();

        // Step 2: Get the Database from DI
        // The database is now resolved from the service provider, with all dependencies configured internally.
        var factory = serviceProvider.GetRequiredService<DatabaseFactory>();
        string masterPassword = "demoMasterPassword123";

        Console.WriteLine($"\nInitializing database at: {dbPath}");
        Console.WriteLine($"Master password: {masterPassword}");
        var db = factory.Create(dbPath, masterPassword);

        // Step 3: Create Tables
        Console.WriteLine("--- Creating Tables ---");
        db.ExecuteSQL("CREATE TABLE users (id INTEGER, name TEXT)");
        string createTestTable = "CREATE TABLE test (id INTEGER PRIMARY KEY, name TEXT, active BOOLEAN, created DATETIME, score REAL, bigNum LONG, price DECIMAL, ulid ULID AUTO, guid GUID AUTO)";
        Console.WriteLine($"Executing: {createTestTable}");
        db.ExecuteSQL(createTestTable);

        // Step 4: Insert Data
        Console.WriteLine("\n--- Inserting Data ---");
        db.ExecuteSQL("INSERT INTO users VALUES (?, ?)", new Dictionary<string, object?> { { "0", 1 }, { "1", "Alice" } });
        db.ExecuteSQL("INSERT INTO users VALUES (?, ?)", new Dictionary<string, object?> { { "0", 2 }, { "1", "Bob" } });
        db.ExecuteSQL("INSERT INTO users VALUES (?, ?)", new Dictionary<string, object?> { { "0", 3 }, { "1", "Charlie" } });
        var newUlid = Ulid.NewUlid().Value;
        Console.WriteLine($"Generated ULID: {newUlid}");
        var parsedUlid = Ulid.Parse(newUlid);
        Console.WriteLine($"Parsed timestamp: {parsedUlid.ToDateTime()}");
        db.ExecuteSQL("INSERT INTO test VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)", 
            new Dictionary<string, object?> { 
                { "0", 1 }, 
                { "1", "Test1" }, 
                { "2", true }, 
                { "3", new DateTime(2023, 1, 1) }, 
                { "4", 10.5 }, 
                { "5", 123456789012345L }, 
                { "6", 99.99m }, 
                { "7", newUlid }, 
                { "8", Guid.NewGuid().ToString() } 
            });
        db.ExecuteSQL("INSERT INTO test VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)", new Dictionary<string, object?> { { "0", 2 }, { "1", "Test2" }, { "2", null }, { "3", null }, { "4", null }, { "5", null }, { "6", null }, { "7", null }, { "8", null } });
        db.ExecuteSQL("INSERT INTO test (id, name) VALUES (?, ?)", new Dictionary<string, object?> { { "0", 3 }, { "1", "AutoTest" } });
        Console.WriteLine("Inserted data");

        // Step 5: Query Data
        Console.WriteLine("\n--- Querying Data ---");

        // Select all test
        Console.WriteLine("Selecting all test:");
        db.ExecuteSQL("SELECT * FROM test");

        // Select test where active = 'true'
        Console.WriteLine("\nSelecting test where active = 'true':");
        db.ExecuteSQL("SELECT * FROM test WHERE active = 'true'");

        // Test UPDATE
        Console.WriteLine("\nTesting UPDATE:");
        db.ExecuteSQL("UPDATE test SET name = ? WHERE id = ?", new Dictionary<string, object?> { { "0", "UpdatedTest" }, { "1", 1 } });
        db.ExecuteSQL("SELECT * FROM test WHERE id = ?", new Dictionary<string, object?> { { "0", 1 } });

        // Test DELETE
        Console.WriteLine("\nTesting DELETE:");
        db.ExecuteSQL("DELETE FROM test WHERE id = ?", new Dictionary<string, object?> { { "0", 2 } });
        db.ExecuteSQL("SELECT * FROM test");

        // Test JOIN
        Console.WriteLine("\nTesting JOIN:");
        db.ExecuteSQL("SELECT test.id, users.name FROM test JOIN users ON test.id = users.id");

        // Test LEFT JOIN
        Console.WriteLine("\nTesting LEFT JOIN:");
        db.ExecuteSQL("SELECT test.id, users.name FROM test LEFT JOIN users ON test.id = users.id");

        // Step 6: Readonly Connection Test
        Console.WriteLine("\n--- Readonly Connection Test ---");
        var dbReadonly = factory.Create(dbPath, masterPassword, true); // Readonly
        Console.WriteLine("Readonly database created");

        // Try to insert in readonly (should fail)
        try
        {
            dbReadonly.ExecuteSQL("INSERT INTO test VALUES (?, ?, ?, ?, ?)", new Dictionary<string, object?> { { "0", 3 }, { "1", "Readonly Test" }, { "2", false }, { "3", new DateTime(2023, 1, 2) }, { "4", 20.0 } });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Insert in readonly failed: {ex.Message}");
        }

        // Query in readonly (allows dirty reads)
        Console.WriteLine("Querying in readonly:");
        dbReadonly.ExecuteSQL("SELECT * FROM test");

        Console.WriteLine("\nDemo completed successfully!");
    }
}

