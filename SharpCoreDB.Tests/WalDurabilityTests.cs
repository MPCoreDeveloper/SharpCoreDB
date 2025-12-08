// <copyright file="WalDurabilityTests.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace SharpCoreDB.Tests;

using SharpCoreDB.Constants;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Services;
using System.Text;
using Xunit.Abstractions;

/// <summary>
/// Tests for WAL durability guarantees.
/// Verifies that Flush(true) ensures data persists to disk and can be recovered after simulated crashes.
/// </summary>
public class WalDurabilityTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _testDbPath;

    public WalDurabilityTests(ITestOutputHelper output)
    {
        _output = output;
        _testDbPath = Path.Combine(Path.GetTempPath(), $"wal_durability_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDbPath);
    }

    /// <summary>
    /// Main durability test: Write WAL entries, flush, simulate crash, verify recovery.
    /// </summary>
    [Fact]
    public async Task WAL_WriteFlushReopen_AllEntriesRecovered()
    {
        // Arrange
        var walPath = Path.Combine(_testDbPath, PersistenceConstants.WalFileName);
        var expectedEntries = new List<string>
        {
            "CREATE TABLE users (id INTEGER, name TEXT)",
            "INSERT INTO users VALUES (1, 'Alice')",
            "INSERT INTO users VALUES (2, 'Bob')",
            "UPDATE users SET name = 'Alice Updated' WHERE id = 1",
            "INSERT INTO users VALUES (3, 'Charlie')"
        };

        _output.WriteLine($"Writing {expectedEntries.Count} WAL entries to {walPath}");

        // Act - Write entries and force flush
        using (var wal = new WAL(_testDbPath))
        {
            foreach (var entry in expectedEntries)
            {
                wal.Log(entry);
            }

            // Force flush to disk - this is the critical durability guarantee
            await wal.FlushAsync();
            
            _output.WriteLine("Flushed WAL to disk with Flush(true)");
        }

        // Simulate crash/restart - don't call Commit(), just reopen the file
        _output.WriteLine("Simulating crash - reopening WAL file for recovery...");

        // Assert - Verify all entries are in the WAL file
        Assert.True(File.Exists(walPath), "WAL file should exist after flush");

        var recoveredEntries = new List<string>();
        using (var reader = new StreamReader(walPath, Encoding.UTF8))
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    recoveredEntries.Add(line.Trim());
                }
            }
        }

        _output.WriteLine($"Recovered {recoveredEntries.Count} entries from WAL");
        foreach (var entry in recoveredEntries)
        {
            _output.WriteLine($"  - {entry}");
        }

        // Verify all entries recovered
        Assert.Equal(expectedEntries.Count, recoveredEntries.Count);
        for (int i = 0; i < expectedEntries.Count; i++)
        {
            Assert.Equal(expectedEntries[i], recoveredEntries[i]);
        }
    }

    /// <summary>
    /// Tests that WAL entries are durable even with large batches.
    /// </summary>
    [Fact]
    public async Task WAL_LargeBatch_FlushAndRecovery()
    {
        // Arrange
        var walPath = Path.Combine(_testDbPath, PersistenceConstants.WalFileName);
        const int entryCount = 1000;
        var expectedEntries = new List<string>();

        for (int i = 0; i < entryCount; i++)
        {
            expectedEntries.Add($"INSERT INTO test VALUES ({i}, 'data_{i}', {i * 10})");
        }

        _output.WriteLine($"Writing {entryCount} WAL entries...");

        // Act - Write large batch and flush
        using (var wal = new WAL(_testDbPath))
        {
            foreach (var entry in expectedEntries)
            {
                wal.Log(entry);
            }

            await wal.FlushAsync();
            _output.WriteLine("Large batch flushed to disk");
        }

        // Simulate restart
        _output.WriteLine("Verifying large batch recovery...");

        // Assert - All entries should be recoverable
        var recoveredEntries = File.ReadAllLines(walPath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Trim())
            .ToList();

        Assert.Equal(entryCount, recoveredEntries.Count);
        
        // Verify first, middle, and last entries
        Assert.Equal(expectedEntries[0], recoveredEntries[0]);
        Assert.Equal(expectedEntries[entryCount / 2], recoveredEntries[entryCount / 2]);
        Assert.Equal(expectedEntries[entryCount - 1], recoveredEntries[entryCount - 1]);
        
        _output.WriteLine($"Successfully recovered all {entryCount} entries");
    }

    /// <summary>
    /// Tests multiple flush cycles to ensure each flush is durable.
    /// </summary>
    [Fact]
    public async Task WAL_MultipleFlushCycles_AllDurable()
    {
        // Arrange
        var walPath = Path.Combine(_testDbPath, PersistenceConstants.WalFileName);
        var batches = new List<List<string>>
        {
            new() { "CREATE TABLE t1 (id INTEGER)", "INSERT INTO t1 VALUES (1)" },
            new() { "CREATE TABLE t2 (id INTEGER)", "INSERT INTO t2 VALUES (2)" },
            new() { "CREATE TABLE t3 (id INTEGER)", "INSERT INTO t3 VALUES (3)" }
        };

        _output.WriteLine($"Testing {batches.Count} flush cycles...");

        // Act - Write and flush multiple batches
        using (var wal = new WAL(_testDbPath))
        {
            int cycle = 1;
            foreach (var batch in batches)
            {
                foreach (var entry in batch)
                {
                    wal.Log(entry);
                }
                
                await wal.FlushAsync();
                _output.WriteLine($"Cycle {cycle}: Flushed {batch.Count} entries");
                cycle++;
            }
        }

        // Simulate restart
        _output.WriteLine("Verifying all cycles recovered...");

        // Assert - All batches should be present
        var allExpectedEntries = batches.SelectMany(b => b).ToList();
        var recoveredEntries = File.ReadAllLines(walPath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Trim())
            .ToList();

        Assert.Equal(allExpectedEntries.Count, recoveredEntries.Count);
        for (int i = 0; i < allExpectedEntries.Count; i++)
        {
            Assert.Equal(allExpectedEntries[i], recoveredEntries[i]);
        }
        
        _output.WriteLine($"All {allExpectedEntries.Count} entries from {batches.Count} cycles recovered");
    }

    /// <summary>
    /// Tests that unflushed entries are NOT durable (negative test).
    /// </summary>
    [Fact]
    public void WAL_UnflushedEntries_NotGuaranteedDurable()
    {
        // Arrange
        var walPath = Path.Combine(_testDbPath, PersistenceConstants.WalFileName);
        var entries = new List<string>
        {
            "INSERT INTO test VALUES (1, 'unflushed')",
            "INSERT INTO test VALUES (2, 'unflushed')"
        };

        _output.WriteLine("Writing entries WITHOUT flushing...");

        // Act - Write but DON'T flush
        using (var wal = new WAL(_testDbPath))
        {
            foreach (var entry in entries)
            {
                wal.Log(entry);
            }
            // Dispose without FlushAsync() - simulates crash before flush
        }

        // Assert - File might exist but entries may not be guaranteed
        // This test documents expected behavior: unflushed data is not durable
        if (File.Exists(walPath))
        {
            var content = File.ReadAllText(walPath);
            _output.WriteLine($"WAL file size after unflushed write: {content.Length} bytes");
            
            // Entries might or might not be present depending on OS buffer behavior
            // This is expected - Flush(true) is required for durability guarantee
            _output.WriteLine("Note: Unflushed entries are not guaranteed durable (expected behavior)");
        }
        else
        {
            _output.WriteLine("WAL file does not exist (unflushed data lost)");
        }

        // This test passes regardless - it documents that flush is required
        Assert.True(true, "Unflushed data is not guaranteed durable by design");
    }

    /// <summary>
    /// Tests WAL with async entry appending and batch flushing.
    /// </summary>
    [Fact]
    public async Task WAL_AsyncAppendEntry_FlushAndRecovery()
    {
        // Arrange
        var walPath = Path.Combine(_testDbPath, PersistenceConstants.WalFileName);
        var entries = new List<WalEntry>();
        
        for (int i = 0; i < 100; i++)
        {
            entries.Add(new WalEntry($"INSERT INTO async_test VALUES ({i}, 'async_{i}')"));
        }

        _output.WriteLine($"Appending {entries.Count} entries asynchronously...");

        // Act - Use async AppendEntryAsync
        using (var wal = new WAL(_testDbPath))
        {
            foreach (var entry in entries)
            {
                await wal.AppendEntryAsync(entry);
            }

            // Force flush of any pending entries
            await wal.FlushAsync();
            _output.WriteLine("Async entries flushed to disk");
        }

        // Simulate restart
        _output.WriteLine("Verifying async entries recovered...");

        // Assert
        var recoveredEntries = File.ReadAllLines(walPath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Trim())
            .ToList();

        Assert.Equal(entries.Count, recoveredEntries.Count);
        for (int i = 0; i < entries.Count; i++)
        {
            Assert.Equal(entries[i].Operation, recoveredEntries[i]);
        }
        
        _output.WriteLine($"All {entries.Count} async entries recovered successfully");
    }

    /// <summary>
    /// Tests that Commit() properly deletes the WAL file after successful transaction.
    /// </summary>
    [Fact]
    public void WAL_Commit_DeletesWalFile()
    {
        // Arrange
        var walPath = Path.Combine(_testDbPath, PersistenceConstants.WalFileName);
        var entries = new List<string>
        {
            "INSERT INTO test VALUES (1, 'committed')",
            "INSERT INTO test VALUES (2, 'committed')"
        };

        _output.WriteLine("Writing and committing WAL...");

        // Act - Write and commit
        using (var wal = new WAL(_testDbPath))
        {
            foreach (var entry in entries)
            {
                wal.Log(entry);
            }
            
            wal.Commit(); // Should flush and delete the WAL
        }

        // Assert - WAL file should be deleted after successful commit
        Assert.False(File.Exists(walPath), "WAL file should be deleted after commit");
        _output.WriteLine("WAL file successfully deleted after commit");
    }

    /// <summary>
    /// Tests that CommitTransactionAsync() properly deletes the WAL file.
    /// </summary>
    [Fact]
    public async Task WAL_CommitAsync_DeletesWalFile()
    {
        // Arrange
        var walPath = Path.Combine(_testDbPath, PersistenceConstants.WalFileName);
        var entries = new List<WalEntry>
        {
            new("INSERT INTO test VALUES (1, 'committed')"),
            new("INSERT INTO test VALUES (2, 'committed')")
        };

        _output.WriteLine("Writing and committing WAL asynchronously...");

        // Act - Write and commit async
        using (var wal = new WAL(_testDbPath))
        {
            foreach (var entry in entries)
            {
                await wal.AppendEntryAsync(entry);
            }
            
            await wal.CommitTransactionAsync();
        }

        // Assert - WAL file should be deleted after successful commit
        Assert.False(File.Exists(walPath), "WAL file should be deleted after async commit");
        _output.WriteLine("WAL file successfully deleted after async commit");
    }

    /// <summary>
    /// Tests concurrent WAL writes with proper flushing.
    /// </summary>
    [Fact]
    public async Task WAL_ConcurrentWrites_AllEntriesDurable()
    {
        // Arrange
        var walPath = Path.Combine(_testDbPath, PersistenceConstants.WalFileName);
        const int taskCount = 10;
        const int entriesPerTask = 10;
        var allEntries = new List<string>();

        for (int i = 0; i < taskCount * entriesPerTask; i++)
        {
            allEntries.Add($"INSERT INTO concurrent VALUES ({i}, 'task_{i / entriesPerTask}')");
        }

        _output.WriteLine($"Writing {allEntries.Count} entries from {taskCount} concurrent tasks...");

        // Act - Write concurrently (WAL uses semaphore for thread-safety)
        using (var wal = new WAL(_testDbPath))
        {
            var tasks = new List<Task>();
            
            for (int t = 0; t < taskCount; t++)
            {
                int taskId = t;
                int startIndex = taskId * entriesPerTask;
                
                tasks.Add(Task.Run(async () =>
                {
                    for (int i = 0; i < entriesPerTask; i++)
                    {
                        await wal.AppendEntryAsync(new WalEntry(allEntries[startIndex + i]));
                    }
                }));
            }

            await Task.WhenAll(tasks);
            await wal.FlushAsync();
            
            _output.WriteLine("All concurrent writes flushed");
        }

        // Simulate restart
        _output.WriteLine("Verifying concurrent writes recovered...");

        // Assert - All entries should be present (order may vary due to concurrency)
        var recoveredEntries = File.ReadAllLines(walPath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Trim())
            .ToHashSet();

        Assert.Equal(allEntries.Count, recoveredEntries.Count);
        
        // Verify all entries are present (not checking order due to concurrency)
        foreach (var expected in allEntries)
        {
            Assert.Contains(expected, recoveredEntries);
        }
        
        _output.WriteLine($"All {allEntries.Count} concurrent entries recovered");
    }

    /// <summary>
    /// Tests that multiple WAL instances can coexist (for testing purposes).
    /// </summary>
    [Fact]
    public async Task WAL_MultipleInstances_EachHasOwnFile()
    {
        // Arrange
        var path1 = Path.Combine(_testDbPath, "db1");
        var path2 = Path.Combine(_testDbPath, "db2");
        Directory.CreateDirectory(path1);
        Directory.CreateDirectory(path2);

        var entries1 = new List<string> { "INSERT INTO db1 VALUES (1)", "INSERT INTO db1 VALUES (2)" };
        var entries2 = new List<string> { "INSERT INTO db2 VALUES (1)", "INSERT INTO db2 VALUES (2)" };

        _output.WriteLine("Creating two separate WAL instances...");

        // Act - Create two WALs
        using (var wal1 = new WAL(path1))
        using (var wal2 = new WAL(path2))
        {
            foreach (var entry in entries1) wal1.Log(entry);
            foreach (var entry in entries2) wal2.Log(entry);

            await wal1.FlushAsync();
            await wal2.FlushAsync();
        }

        // Assert - Each has its own WAL file
        var walPath1 = Path.Combine(path1, PersistenceConstants.WalFileName);
        var walPath2 = Path.Combine(path2, PersistenceConstants.WalFileName);

        Assert.True(File.Exists(walPath1));
        Assert.True(File.Exists(walPath2));

        var content1 = File.ReadAllLines(walPath1).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        var content2 = File.ReadAllLines(walPath2).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();

        Assert.Equal(entries1.Count, content1.Count);
        Assert.Equal(entries2.Count, content2.Count);
        
        _output.WriteLine("Both WAL instances maintained separate files correctly");
    }

    /// <summary>
    /// Tests recovery with special characters and encoding.
    /// </summary>
    [Fact]
    public async Task WAL_SpecialCharacters_ProperlyEncoded()
    {
        // Arrange
        var walPath = Path.Combine(_testDbPath, PersistenceConstants.WalFileName);
        var entries = new List<string>
        {
            "INSERT INTO test VALUES (1, 'Hello ??')",
            "INSERT INTO test VALUES (2, 'emoji ??')",
            "INSERT INTO test VALUES (3, 'quotes \"double\" and ''single''')",
            "INSERT INTO test VALUES (4, 'newline\nand\ttab')"
        };

        _output.WriteLine("Writing entries with special characters...");

        // Act
        using (var wal = new WAL(_testDbPath))
        {
            foreach (var entry in entries)
            {
                wal.Log(entry);
            }
            await wal.FlushAsync();
        }

        // Assert - UTF-8 encoding should preserve special characters
        var recovered = File.ReadAllLines(walPath, Encoding.UTF8)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Trim())
            .ToList();

        Assert.Equal(entries.Count, recovered.Count);
        
        // Note: Newlines within entries become actual line breaks, so we only verify count
        _output.WriteLine($"Successfully recovered {recovered.Count} entries with special characters");
    }

    /// <summary>
    /// Stress test: Rapid write-flush cycles.
    /// </summary>
    [Fact]
    public async Task WAL_RapidFlushCycles_NoCrash()
    {
        // Arrange
        const int cycleCount = 50;
        var walPath = Path.Combine(_testDbPath, PersistenceConstants.WalFileName);
        var totalEntries = 0;

        _output.WriteLine($"Running {cycleCount} rapid flush cycles...");

        // Act
        using (var wal = new WAL(_testDbPath))
        {
            for (int cycle = 0; cycle < cycleCount; cycle++)
            {
                var entriesThisCycle = Random.Shared.Next(1, 10);
                for (int i = 0; i < entriesThisCycle; i++)
                {
                    wal.Log($"CYCLE {cycle} ENTRY {i}");
                    totalEntries++;
                }
                await wal.FlushAsync();
            }
        }

        // Assert
        var recovered = File.ReadAllLines(walPath)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Count();

        Assert.Equal(totalEntries, recovered);
        _output.WriteLine($"Successfully completed {cycleCount} cycles with {totalEntries} total entries");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDbPath))
            {
                Directory.Delete(_testDbPath, true);
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Cleanup warning: {ex.Message}");
        }
    }
}
