// Quick Debug Test - Run this to see EXACT error
using SharpCoreDB.Benchmarks.Infrastructure;
using SharpCoreDB.Benchmarks.Comparative;
using Microsoft.Data.Sqlite;
using LiteDB;
using System.Diagnostics;

namespace SharpCoreDB.DebugBenchmark;

public class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("==============================================");
        Console.WriteLine("  Quick10kComparison DEBUG TEST");
        Console.WriteLine("==============================================\n");

        var benchmark = new Quick10kComparison();

        try
        {
            Console.WriteLine("Step 1: Running GlobalSetup...");
            benchmark.Setup();
            Console.WriteLine("? GlobalSetup SUCCESS\n");
            
            Console.WriteLine("Step 2: Running IterationSetup...");
            benchmark.IterationSetup();
            Console.WriteLine("? IterationSetup SUCCESS\n");
            
            // Test each benchmark individually
            Console.WriteLine("Step 3: Testing SharpCoreDB (No Encryption)...");
            var sw = Stopwatch.StartNew();
            var result1 = benchmark.SharpCoreDB_NoEncrypt_10K();
            sw.Stop();
            Console.WriteLine($"? SharpCoreDB_NoEncrypt_10K SUCCESS: {result1} records in {sw.ElapsedMilliseconds}ms\n");
            
            Console.WriteLine("Step 4: Testing SharpCoreDB (Encrypted)...");
            benchmark.IterationSetup();
            sw.Restart();
            var result2 = benchmark.SharpCoreDB_Encrypted_10K();
            sw.Stop();
            Console.WriteLine($"? SharpCoreDB_Encrypted_10K SUCCESS: {result2} records in {sw.ElapsedMilliseconds}ms\n");
            
            Console.WriteLine("Step 5: Testing SQLite (Memory)...");
            benchmark.IterationSetup();
            sw.Restart();
            var result3 = benchmark.SQLite_Memory_10K();
            sw.Stop();
            Console.WriteLine($"? SQLite_Memory_10K SUCCESS: {result3} records in {sw.ElapsedMilliseconds}ms\n");
            
            Console.WriteLine("Step 6: Testing SQLite (File + WAL)...");
            benchmark.IterationSetup();
            sw.Restart();
            var result4 = benchmark.SQLite_File_WAL_10K();
            sw.Stop();
            Console.WriteLine($"? SQLite_File_WAL_10K SUCCESS: {result4} records in {sw.ElapsedMilliseconds}ms\n");
            
            Console.WriteLine("Step 7: Testing LiteDB...");
            benchmark.IterationSetup();
            sw.Restart();
            var result5 = benchmark.LiteDB_10K();
            sw.Stop();
            Console.WriteLine($"? LiteDB_10K SUCCESS: {result5} records in {sw.ElapsedMilliseconds}ms\n");
            
            Console.WriteLine("==============================================");
            Console.WriteLine("  ALL TESTS PASSED!");
            Console.WriteLine("==============================================");
            Console.WriteLine($"\nResults:");
            Console.WriteLine($"  SharpCoreDB (No Encryption): {result1} records");
            Console.WriteLine($"  SharpCoreDB (Encrypted): {result2} records");
            Console.WriteLine($"  SQLite (Memory): {result3} records");
            Console.WriteLine($"  SQLite (File + WAL): {result4} records");
            Console.WriteLine($"  LiteDB: {result5} records");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n? ERROR: {ex.GetType().Name}");
            Console.WriteLine($"Message: {ex.Message}");
            Console.WriteLine($"\nStack Trace:");
            Console.WriteLine(ex.StackTrace);
            
            if (ex.InnerException != null)
            {
                Console.WriteLine($"\nInner Exception: {ex.InnerException.GetType().Name}");
                Console.WriteLine($"Message: {ex.InnerException.Message}");
                Console.WriteLine($"Stack Trace:");
                Console.WriteLine(ex.InnerException.StackTrace);
            }
            Console.ResetColor();
        }
        finally
        {
            Console.WriteLine("\nStep 8: Running Cleanup...");
            benchmark.Cleanup();
            Console.WriteLine("? Cleanup complete");
        }

        Console.WriteLine("\n\nPress any key to exit...");
        Console.ReadKey();
    }
}
