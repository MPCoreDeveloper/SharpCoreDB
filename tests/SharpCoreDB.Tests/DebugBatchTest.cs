using Xunit;
using SharpCoreDB;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.DependencyInjection;

namespace SharpCoreDB.Tests;

[Trait("Category", "Debug")]
public class DebugBatchTest
{
    private readonly ITestOutputHelper _output;

    public DebugBatchTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Debug_ExecuteBatchSQL_ShouldInsertAllRows()
    {
        // Arrange
        var dbPath = Path.Combine(Path.GetTempPath(), $"test_debug_{Guid.NewGuid()}");
        
        _output.WriteLine($"Database path: {dbPath}");
        
        try
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSharpCoreDB();
            var services = serviceCollection.BuildServiceProvider();
            var factory = services.GetRequiredService<DatabaseFactory>();

            // ? FIXED: Using SingleFile mode with refactored engine-based batch insert
            var options = new DatabaseOptions
            {
                DatabaseConfig = new DatabaseConfig
                {
                    NoEncryptMode = true,
                    SqlValidationMode = SharpCoreDB.Services.SqlQueryValidator.ValidationMode.Disabled,
                    StrictParameterValidation = false
                },
                CreateImmediately = true,
                StorageMode = StorageMode.Directory  // ? Use Directory mode (works correctly with engine-based batch insert)
            };

            var db = factory.CreateWithOptions(dbPath, "password", options);
            _output.WriteLine($"Database instance created, type={db.GetType().Name}");

            // Create test table
            db.ExecuteSQL(@"
                CREATE TABLE bench_records (
                    id INT PRIMARY KEY,
                    name TEXT NOT NULL,
                    email TEXT NOT NULL,
                    age INT NOT NULL,
                    salary DECIMAL NOT NULL,
                    created DATETIME NOT NULL
                )
            ");
            _output.WriteLine("Table created successfully");

            // Act - insert 5 rows for easier debugging
            var inserts = new List<string>();
            for (int i = 0; i < 5; i++)
            {
                var sql = $"INSERT INTO bench_records (id, name, email, age, salary, created) VALUES ({i}, 'User{i}', 'user{i}@test.com', {20 + i}, {30000 + i}, '2025-01-01')";
                _output.WriteLine($"SQL[{i}]: {sql}");
                inserts.Add(sql);
            }

            _output.WriteLine($"\nCalling ExecuteBatchSQL with {inserts.Count} statements...");
            try
            {
                db.ExecuteBatchSQL(inserts);
                _output.WriteLine("ExecuteBatchSQL completed.");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"ERROR in ExecuteBatchSQL: {ex.Message}");
                _output.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }

            // Flush if needed
            if (db is Database concreteDb)
            {
                concreteDb.FlushPendingWalStatements();
                concreteDb.Flush();
                _output.WriteLine("Flushed database (Database type).");
            }
            
            var storageField = db.GetType()
                .GetField("_storageProvider", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (storageField?.GetValue(db) is SharpCoreDB.Storage.IStorageProvider storage)
            {
                storage.FlushAsync().GetAwaiter().GetResult();
                _output.WriteLine("Flushed storage provider after batch.");
            }

            // Assert
            var results = db.ExecuteQuery("SELECT * FROM bench_records ORDER BY id");
            
            _output.WriteLine($"\nQuery returned {results.Count} rows:");
            for (int i = 0; i < results.Count; i++)
            {
                var row = results[i];
                _output.WriteLine($"  Row[{i}]: keys={row.Count}, data={string.Join(", ", row.Select(kv => $"{kv.Key}={kv.Value}"))}");
            }
            
            Assert.Equal(5, results.Count);
        }
        finally
        {
            // Cleanup
            try
            {
                if (Directory.Exists(dbPath))
                    Directory.Delete(dbPath, true);
            }
            catch { }
        }
    }
}
