using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Interfaces;
using System.Data.SQLite;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Simple performance test for 100k records comparison.
/// </summary>
public static class PerformanceTest
{
    public static void RunPerformanceTest()
    {
        const int recordCount = 10_000;
        Console.WriteLine("=== SharpCoreDB vs SQLite Performance Test ===");
        Console.WriteLine($"Testing with {recordCount:N0} records");
        Console.WriteLine();

        // Test SharpCoreDB
        var sharpResults = TestSharpCoreDB(recordCount);
        
        // Test SQLite
        var sqliteResults = TestSQLite(recordCount);
        
        // Display results
        Console.WriteLine("\n=== Results ===");
        Console.WriteLine($"{"Operation",-30} {"SharpCoreDB",-15} {"SQLite",-15} {"Difference",-15}");
        Console.WriteLine(new string('-', 75));
        
        DisplayComparison("Insert 100k records", sharpResults.InsertTime, sqliteResults.InsertTime);
        DisplayComparison("Select with WHERE", sharpResults.SelectTime, sqliteResults.SelectTime);
        DisplayComparison("Select 1000 records", sharpResults.SelectMultipleTime, sqliteResults.SelectMultipleTime);
        
        Console.WriteLine();
        Console.WriteLine("Note: Results may vary based on system performance.");
    }

    private static void DisplayComparison(string operation, double sharpTime, double sqliteTime)
    {
        var difference = sharpTime - sqliteTime;
        var percentDiff = sqliteTime > 0 ? (difference / sqliteTime) * 100 : 0;
        var diffStr = difference > 0 ? $"+{difference:F0}ms ({percentDiff:+0.0}%)" : $"{difference:F0}ms ({percentDiff:0.0}%)";
        
        Console.WriteLine($"{operation,-30} {sharpTime:F0}ms{"",-8} {sqliteTime:F0}ms{"",-8} {diffStr}");
    }

    private static (double InsertTime, double SelectTime, double SelectMultipleTime) TestSharpCoreDB(int recordCount)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"perf_test_sharp_{Guid.NewGuid()}");
        
        try
        {
            var services = new ServiceCollection();
            services.AddSharpCoreDB();
            var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<DatabaseFactory>();
            var db = factory.Create(dbPath, "perfTestPassword");
            
            db.ExecuteSQL("CREATE TABLE time_entries (id INTEGER PRIMARY KEY, project TEXT, task TEXT, start_time DATETIME, duration INTEGER, user TEXT)");
            
            // Test Insert
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < recordCount; i++)
            {
                db.ExecuteSQL($"INSERT INTO time_entries VALUES ('{i}', 'Project{i % 100}', 'Task{i % 20}', '2024-01-{(i % 28) + 1:00} 09:00:00', '480', 'User{i % 10}')");
            }
            sw.Stop();
            var insertTime = sw.Elapsed.TotalMilliseconds;
            Console.WriteLine($"SharpCoreDB Insert: {insertTime:F0}ms");
            
            // Test Select with WHERE
            sw.Restart();
            for (int i = 0; i < 10; i++)
            {
                db.ExecuteSQL("SELECT * FROM time_entries WHERE project = 'Project50'");
            }
            sw.Stop();
            var selectTime = sw.Elapsed.TotalMilliseconds / 10;
            Console.WriteLine($"SharpCoreDB Select WHERE: {selectTime:F0}ms (avg of 10)");
            
            // Test Select multiple records
            sw.Restart();
            db.ExecuteSQL("SELECT * FROM time_entries WHERE duration = '480'");
            sw.Stop();
            var selectMultipleTime = sw.Elapsed.TotalMilliseconds;
            Console.WriteLine($"SharpCoreDB Select Multiple: {selectMultipleTime:F0}ms");
            
            return (insertTime, selectTime, selectMultipleTime);
        }
        finally
        {
            if (Directory.Exists(dbPath))
                Directory.Delete(dbPath, true);
        }
    }

    private static (double InsertTime, double SelectTime, double SelectMultipleTime) TestSQLite(int recordCount)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"perf_test_sqlite_{Guid.NewGuid()}.db");
        
        try
        {
            using var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;");
            conn.Open();
            
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "CREATE TABLE time_entries (id INTEGER PRIMARY KEY, project TEXT, task TEXT, start_time DATETIME, duration INTEGER, user TEXT)";
                cmd.ExecuteNonQuery();
            }
            
            // Test Insert
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < recordCount; i++)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"INSERT INTO time_entries VALUES ({i}, 'Project{i % 100}', 'Task{i % 20}', '2024-01-{(i % 28) + 1:00} 09:00:00', 480, 'User{i % 10}')";
                cmd.ExecuteNonQuery();
            }
            sw.Stop();
            var insertTime = sw.Elapsed.TotalMilliseconds;
            Console.WriteLine($"SQLite Insert: {insertTime:F0}ms");
            
            // Test Select with WHERE
            sw.Restart();
            for (int i = 0; i < 10; i++)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT * FROM time_entries WHERE project = 'Project50'";
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) { }
            }
            sw.Stop();
            var selectTime = sw.Elapsed.TotalMilliseconds / 10;
            Console.WriteLine($"SQLite Select WHERE: {selectTime:F0}ms (avg of 10)");
            
            // Test Select multiple records
            sw.Restart();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT * FROM time_entries WHERE duration = 480";
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) { }
            }
            sw.Stop();
            var selectMultipleTime = sw.Elapsed.TotalMilliseconds;
            Console.WriteLine($"SQLite Select Multiple: {selectMultipleTime:F0}ms");
            
            return (insertTime, selectTime, selectMultipleTime);
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }
}
