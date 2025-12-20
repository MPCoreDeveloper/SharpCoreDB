using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Interfaces;

Console.WriteLine("=== Diagnosing INSERT Benchmark Failure ===\n");

var services = new ServiceCollection();
services.AddSharpCoreDB();
var provider = services.BuildServiceProvider();
var factory = provider.GetRequiredService<DatabaseFactory>();

var testPath = Path.Combine(Path.GetTempPath(), $"test_insert_{Guid.NewGuid()}");
Directory.CreateDirectory(testPath);

try
{
    var config = new DatabaseConfig
    {
        NoEncryptMode = true,
        StorageEngineType = StorageEngineType.PageBased,
        EnablePageCache = true,
        PageCacheCapacity = 1000,
        SqlValidationMode = SharpCoreDB.Services.SqlQueryValidator.ValidationMode.Disabled,
        StrictParameterValidation = false
    };

    var db = (Database)factory.Create(testPath, "password", false, config);
    
    Console.WriteLine("1. Creating table...");
    db.ExecuteSQL(@"CREATE TABLE bench_records (
        id INTEGER PRIMARY KEY,
        name TEXT,
        email TEXT,
        age INTEGER,
        salary DECIMAL,
        created DATETIME
    )");
    Console.WriteLine("   ✅ Table created\n");
    
    Console.WriteLine("2. Pre-populating 10K records (IDs 0-9999)...");
    var prepopulate = new List<string>();
    for (int i = 0; i < 10000; i++)
    {
        prepopulate.Add($@"INSERT INTO bench_records (id, name, email, age, salary, created) 
            VALUES ({i}, 'User{i}', 'user{i}@test.com', {20 + (i % 50)}, {30000 + (i % 70000)}, '2025-01-01')");
    }
    db.ExecuteBatchSQL(prepopulate);
    Console.WriteLine("   ✅ Pre-population complete\n");
    
    Console.WriteLine("3. Testing INSERT with NEW IDs (10000-19999)...");
    var newInserts = new List<string>();
    for (int i = 0; i < 10000; i++)
    {
        int id = 10000 + i;
        newInserts.Add($@"INSERT INTO bench_records (id, name, email, age, salary, created) 
            VALUES ({id}, 'NewUser{id}', 'newuser{id}@test.com', {20 + (i % 50)}, {30000 + (i % 70000)}, '2025-01-01')");
    }
    db.ExecuteBatchSQL(newInserts);
    Console.WriteLine("   ✅ First INSERT OK\n");
    
    Console.WriteLine("4. Deleting NEW records (DELETE WHERE id >= 10000)...");
    db.ExecuteSQL("DELETE FROM bench_records WHERE id >= 10000");
    Console.WriteLine("   ✅ DELETE OK\n");
    
    Console.WriteLine("5. Re-inserting NEW IDs (simulating IterationSetup + Benchmark)...");
    db.ExecuteBatchSQL(newInserts);
    Console.WriteLine("   ✅ Second INSERT OK\n");
    
    Console.WriteLine("6. Third iteration (DELETE + INSERT again)...");
    db.ExecuteSQL("DELETE FROM bench_records WHERE id >= 10000");
    db.ExecuteBatchSQL(newInserts);
    Console.WriteLine("   ✅ Third INSERT OK\n");
    
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("🎉 ALL TESTS PASSED!");
    Console.WriteLine("   INSERT benchmark pattern works correctly!");
    Console.ResetColor();
    
    db.Dispose();
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"\n❌ FAILURE at step!");
    Console.WriteLine($"   Exception: {ex.GetType().Name}");
    Console.WriteLine($"   Message: {ex.Message}");
    Console.WriteLine($"\n   Stack Trace:");
    Console.WriteLine($"   {ex.StackTrace}");
    
    if (ex.InnerException != null)
    {
        Console.WriteLine($"\n   Inner Exception: {ex.InnerException.GetType().Name}");
        Console.WriteLine($"   {ex.InnerException.Message}");
    }
    Console.ResetColor();
}
finally
{
    try { Directory.Delete(testPath, true); } catch { }
}

Console.WriteLine("\nTest complete.");
