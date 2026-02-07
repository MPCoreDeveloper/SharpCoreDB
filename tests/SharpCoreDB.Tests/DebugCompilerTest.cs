// Quick debug test to check WHERE clause compilation
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.DataStructures;

namespace SharpCoreDB.Tests;

[Trait("Category", "Debug")]
public class DebugCompilerTest
{
    [Fact]
    public void Debug_ComplexWhere()
    {
        var testDbPath = Path.Combine(Path.GetTempPath(), $"debug_test_{Guid.NewGuid()}");
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var serviceProvider = services.BuildServiceProvider();
        
        var factory = serviceProvider.GetRequiredService<DatabaseFactory>();
        var db = factory.Create(testDbPath, "test123");

        try
        {
            db.ExecuteSQL("CREATE TABLE inventory (id INTEGER, product TEXT, quantity INTEGER, price DECIMAL)");
            db.ExecuteSQL("INSERT INTO inventory VALUES (1, 'Widget', 50, 9.99)");
            db.ExecuteSQL("INSERT INTO inventory VALUES (2, 'Gadget', 25, 19.99)");
            db.ExecuteSQL("INSERT INTO inventory VALUES (3, 'Doohickey', 100, 4.99)");
            db.ExecuteSQL("INSERT INTO inventory VALUES (4, 'Thingamajig', 10, 14.99)");

            // Debug: Check what's in the table
            var allRows = db.ExecuteQuery("SELECT * FROM inventory");
            Console.WriteLine($"Total rows: {allRows.Count}");
            foreach (var row in allRows)
            {
                Console.WriteLine($"  id={row["id"]}, product={row["product"]}, quantity={row["quantity"]}, price={row["price"]} (type: {row["price"]?.GetType().Name})");
            }

            // Test individual conditions
            var results1 = db.ExecuteQuery("SELECT product FROM inventory WHERE quantity > 20");
            Console.WriteLine($"\nWHERE quantity > 20: {results1.Count} rows");
            foreach (var r in results1) Console.WriteLine($"  {r["product"]}");

            var results2 = db.ExecuteQuery("SELECT product FROM inventory WHERE price < 15");
            Console.WriteLine($"\nWHERE price < 15: {results2.Count} rows");
            foreach (var r in results2) Console.WriteLine($"  {r["product"]}");

            // Test compiled query
            var stmt = db.Prepare("SELECT product FROM inventory WHERE quantity > 20 AND price < 15");
            Console.WriteLine($"\nPrepared statement IsCompiled: {stmt.IsCompiled}");
            Console.WriteLine($"  HasWhereClause: {stmt.CompiledPlan?.HasWhereClause}");
            
            var compiledResults = db.ExecuteCompiledQuery(stmt);
            Console.WriteLine($"\nCompiled WHERE quantity > 20 AND price < 15: {compiledResults.Count} rows");
            foreach (var r in compiledResults) 
            {
                if (r.ContainsKey("product"))
                    Console.WriteLine($"  {r["product"]}");
                else
                    Console.WriteLine($"  Keys: {string.Join(", ", r.Keys)}");
            }
        }
        finally
        {
            if (Directory.Exists(testDbPath))
                Directory.Delete(testDbPath, true);
        }
    }
}
