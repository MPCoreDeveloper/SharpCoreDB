// Example: Using Group Commit WAL in SharpCoreDB
// This example demonstrates the high-performance append-only WAL with group commits

using SharpCoreDB.Services;
using System.Text;

namespace SharpCoreDB.Examples;

/// <summary>
/// Examples of using the GroupCommitWAL for high-throughput write operations.
/// </summary>
public class GroupCommitWalExample
{
    /// <summary>
    /// Example 1: Basic usage with full durability.
    /// </summary>
    public static async Task BasicUsageExample()
    {
        Console.WriteLine("=== Example 1: Basic Usage ===\n");

        // Create WAL with full durability (survives crashes and power failures)
        await using var wal = new GroupCommitWAL(
            dbPath: "./mydb",
            durabilityMode: DurabilityMode.FullSync);

        // Commit some data
        byte[] data1 = Encoding.UTF8.GetBytes("INSERT INTO users VALUES (1, 'Alice')");
        byte[] data2 = Encoding.UTF8.GetBytes("INSERT INTO users VALUES (2, 'Bob')");

        await wal.CommitAsync(data1);
        Console.WriteLine("Committed: Alice");

        await wal.CommitAsync(data2);
        Console.WriteLine("Committed: Bob");

        var (commits, batches, avgBatch, bytes) = wal.GetStatistics();
        Console.WriteLine($"\nStatistics:");
        Console.WriteLine($"  Total commits: {commits}");
        Console.WriteLine($"  Total batches: {batches}");
        Console.WriteLine($"  Average batch size: {avgBatch:F2}");
        Console.WriteLine($"  Total bytes: {bytes}");
    }

    /// <summary>
    /// Example 2: High-concurrency scenario demonstrating group commit benefits.
    /// </summary>
    public static async Task HighConcurrencyExample()
    {
        Console.WriteLine("\n=== Example 2: High Concurrency (100 threads) ===\n");

        await using var wal = new GroupCommitWAL(
            dbPath: "./mydb",
            durabilityMode: DurabilityMode.FullSync,
            maxBatchSize: 100,
            maxBatchDelayMs: 10);

        // Simulate 100 concurrent threads writing
        var tasks = Enumerable.Range(0, 1000).Select(async i =>
        {
            byte[] data = Encoding.UTF8.GetBytes($"INSERT INTO logs VALUES ({i}, 'Event {i}')");
            await wal.CommitAsync(data);
        });

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await Task.WhenAll(tasks);
        stopwatch.Stop();

        var (commits, batches, avgBatch, bytes) = wal.GetStatistics();
        Console.WriteLine($"Completed 1000 commits in {stopwatch.ElapsedMilliseconds} ms");
        Console.WriteLine($"Throughput: {1000.0 / stopwatch.Elapsed.TotalSeconds:F0} commits/sec");
        Console.WriteLine($"\nBatching Statistics:");
        Console.WriteLine($"  Total batches: {batches}");
        Console.WriteLine($"  Average batch size: {avgBatch:F2}");
        Console.WriteLine($"  Batching efficiency: {(1 - (double)batches / commits) * 100:F1}%");
        Console.WriteLine($"  fsync reduction: {commits / batches:F1}x fewer disk flushes");
    }

    /// <summary>
    /// Example 3: Crash recovery - replay committed operations.
    /// </summary>
    public static async Task CrashRecoveryExample()
    {
        Console.WriteLine("\n=== Example 3: Crash Recovery ===\n");

        // Step 1: Write some data
        {
            await using var wal = new GroupCommitWAL("./mydb");
            
            await wal.CommitAsync(Encoding.UTF8.GetBytes("CREATE TABLE users (id INT, name TEXT)"));
            await wal.CommitAsync(Encoding.UTF8.GetBytes("INSERT INTO users VALUES (1, 'Alice')"));
            await wal.CommitAsync(Encoding.UTF8.GetBytes("INSERT INTO users VALUES (2, 'Bob')"));
            
            Console.WriteLine("Committed 3 operations to WAL");
        } // WAL is disposed (simulating normal shutdown or crash)

        // Step 2: Recover after "crash"
        {
            await using var wal = new GroupCommitWAL("./mydb");
            
            var recoveredOps = wal.CrashRecovery();
            Console.WriteLine($"\nRecovered {recoveredOps.Count} operations from WAL:");
            
            foreach (var op in recoveredOps)
            {
                string sql = Encoding.UTF8.GetString(op.Span);
                Console.WriteLine($"  - {sql}");
            }

            // Clear WAL after successful recovery
            await wal.ClearAsync();
            Console.WriteLine("\nWAL cleared after successful recovery");
        }
    }

    /// <summary>
    /// Example 4: Async durability mode for higher throughput.
    /// </summary>
    public static async Task AsyncModeExample()
    {
        Console.WriteLine("\n=== Example 4: Async Durability Mode ===\n");

        await using var wal = new GroupCommitWAL(
            dbPath: "./logs",
            durabilityMode: DurabilityMode.Async, // Faster but may lose recent commits on crash
            maxBatchSize: 500,
            maxBatchDelayMs: 20);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        for (int i = 0; i < 10000; i++)
        {
            byte[] logEntry = Encoding.UTF8.GetBytes($"[{DateTime.UtcNow:O}] Event {i}");
            await wal.CommitAsync(logEntry);
        }

        stopwatch.Stop();

        var (commits, batches, avgBatch, bytes) = wal.GetStatistics();
        Console.WriteLine($"Committed 10,000 log entries in {stopwatch.ElapsedMilliseconds} ms");
        Console.WriteLine($"Throughput: {10000.0 / stopwatch.Elapsed.TotalSeconds:F0} entries/sec");
        Console.WriteLine($"Average batch size: {avgBatch:F2}");
        Console.WriteLine($"\nNote: Async mode is much faster but may lose recent commits on power failure");
    }

    /// <summary>
    /// Example 5: Comparing FullSync vs Async performance.
    /// </summary>
    public static async Task ComparePerformanceExample()
    {
        Console.WriteLine("\n=== Example 5: Performance Comparison ===\n");

        const int NumOperations = 1000;

        // Test FullSync
        {
            await using var wal = new GroupCommitWAL(
                "./test_fullsync",
                DurabilityMode.FullSync,
                maxBatchSize: 100,
                maxBatchDelayMs: 10);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            var tasks = Enumerable.Range(0, NumOperations).Select(async i =>
            {
                await wal.CommitAsync(Encoding.UTF8.GetBytes($"Data {i}"));
            });
            await Task.WhenAll(tasks);

            stopwatch.Stop();
            var (commits, batches, avgBatch, _) = wal.GetStatistics();

            Console.WriteLine($"FullSync Mode:");
            Console.WriteLine($"  Time: {stopwatch.ElapsedMilliseconds} ms");
            Console.WriteLine($"  Throughput: {NumOperations / stopwatch.Elapsed.TotalSeconds:F0} ops/sec");
            Console.WriteLine($"  Average batch size: {avgBatch:F2}");
        }

        // Test Async
        {
            await using var wal = new GroupCommitWAL(
                "./test_async",
                DurabilityMode.Async,
                maxBatchSize: 100,
                maxBatchDelayMs: 10);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            var tasks = Enumerable.Range(0, NumOperations).Select(async i =>
            {
                await wal.CommitAsync(Encoding.UTF8.GetBytes($"Data {i}"));
            });
            await Task.WhenAll(tasks);

            stopwatch.Stop();
            var (commits, batches, avgBatch, _) = wal.GetStatistics();

            Console.WriteLine($"\nAsync Mode:");
            Console.WriteLine($"  Time: {stopwatch.ElapsedMilliseconds} ms");
            Console.WriteLine($"  Throughput: {NumOperations / stopwatch.Elapsed.TotalSeconds:F0} ops/sec");
            Console.WriteLine($"  Average batch size: {avgBatch:F2}");
        }
    }

    /// <summary>
    /// Run all examples.
    /// </summary>
    public static async Task Main(string[] args)
    {
        Console.WriteLine("GroupCommitWAL Examples\n");
        Console.WriteLine("These examples demonstrate the high-performance append-only WAL with group commits.\n");

        try
        {
            await BasicUsageExample();
            await HighConcurrencyExample();
            await CrashRecoveryExample();
            await AsyncModeExample();
            await ComparePerformanceExample();

            Console.WriteLine("\n=== All examples completed successfully! ===");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nError: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }
}
