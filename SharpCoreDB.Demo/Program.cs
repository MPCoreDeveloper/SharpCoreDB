using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;

// Serilog setup stub
// Add Serilog packages to your project:
// dotnet add package Serilog
// dotnet add package Serilog.Sinks.Console
// dotnet add package Serilog.Extensions.Logging
using Serilog;
using System.Diagnostics;

class Program
{
    static void Main(string[] args)
    {
        // Configure Serilog
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .CreateLogger();

        try
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
            // Add Serilog as the logging provider
            services.AddLogging(loggingBuilder => loggingBuilder.AddSerilog());
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

            // Performance demo
            Console.WriteLine("\n--- Performance Demo: JOIN with 10k records ---");
            db.ExecuteSQL("CREATE TABLE users_large (id INTEGER PRIMARY KEY, name TEXT)");
            db.ExecuteSQL("CREATE TABLE projects (id INTEGER PRIMARY KEY, name TEXT, user_id INTEGER)");

            // Insert 1000 users
            for (int i = 1; i <= 1000; i++)
            {
                db.ExecuteSQL("INSERT INTO users_large VALUES (?, ?)", new Dictionary<string, object?> { { "0", i }, { "1", $"User{i}" } });
            }

            // Insert 10000 projects, each user has 10
            for (int i = 1; i <= 10000; i++)
            {
                int userId = ((i - 1) % 1000) + 1;
                db.ExecuteSQL("INSERT INTO projects VALUES (?, ?, ?)", new Dictionary<string, object?> { { "0", i }, { "1", $"Project{i}" }, { "2", userId } });
            }

            Console.WriteLine("Inserted 1000 users and 10000 projects");

            // Time the JOIN without cache (first time)
            var stopwatch = Stopwatch.StartNew();
            var results = db.ExecuteQuery("SELECT projects.name, users_large.name FROM projects LEFT JOIN users_large ON projects.user_id = users_large.id");
            stopwatch.Stop();
            Console.WriteLine($"JOIN without cache: {results.Count} rows in {stopwatch.ElapsedMilliseconds} ms");

            // With cache (second time)
            stopwatch = Stopwatch.StartNew();
            results = db.ExecuteQuery("SELECT projects.name, users_large.name FROM projects LEFT JOIN users_large ON projects.user_id = users_large.id");
            stopwatch.Stop();
            Console.WriteLine($"JOIN with cache: {results.Count} rows in {stopwatch.ElapsedMilliseconds} ms");

            Console.WriteLine("\nDemo completed successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
        }
        finally
        {
            // Ensure to flush and stop the logger
            Log.CloseAndFlush();
        }
    }
}

