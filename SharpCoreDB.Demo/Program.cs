using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Interfaces;

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
        db.ExecuteSQL("INSERT INTO users VALUES ('1', 'Alice')");
        db.ExecuteSQL("INSERT INTO users VALUES ('2', 'Bob')");
        db.ExecuteSQL("INSERT INTO users VALUES ('3', 'Charlie')");
        var newUlid = Ulid.NewUlid().Value;
        Console.WriteLine($"Generated ULID: {newUlid}");
        var parsedUlid = Ulid.Parse(newUlid);
        Console.WriteLine($"Parsed timestamp: {parsedUlid.ToDateTime()}");
        db.ExecuteSQL($"INSERT INTO test VALUES ('1', 'Test1', 'true', '2023-01-01', '10.5', '123456789012345', '99.99', '{newUlid}', '{Guid.NewGuid()}')");
        db.ExecuteSQL("INSERT INTO test VALUES ('2', 'Test2', NULL, NULL, NULL, NULL, NULL, NULL, NULL)");
        db.ExecuteSQL("INSERT INTO test (id, name) VALUES ('3', 'AutoTest')");
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
        db.ExecuteSQL("UPDATE test SET name = 'UpdatedTest' WHERE id = '1'");
        db.ExecuteSQL("SELECT * FROM test WHERE id = '1'");

        // Test DELETE
        Console.WriteLine("\nTesting DELETE:");
        db.ExecuteSQL("DELETE FROM test WHERE id = '2'");
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
            dbReadonly.ExecuteSQL("INSERT INTO test VALUES ('3', 'Readonly Test', 'false', '2023-01-02', '20.0')");
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

