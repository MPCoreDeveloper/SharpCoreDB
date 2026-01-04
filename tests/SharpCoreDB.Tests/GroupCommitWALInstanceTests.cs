using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SharpCoreDB.Tests;

/// <summary>
/// Tests to verify the GroupCommitWAL instance-specific file locking fix.
/// Ensures multiple Database instances can coexist without IOException.
/// </summary>
public class GroupCommitWALInstanceTests
{
    private readonly string testDbPath;

    public GroupCommitWALInstanceTests()
    {
        testDbPath = Path.Combine(Path.GetTempPath(), $"wal_instance_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDbPath);
    }

    /// <summary>
    /// Test 1: Multiple Database instances can coexist at same path.
    /// BEFORE FIX: IOException ("file is being used by another process")
    /// AFTER FIX: Should work without errors
    /// </summary>
    [Fact]
    public void MultipleInstances_SamePath_NoConflict()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<DatabaseFactory>();

        var config = new DatabaseConfig
        {
            UseGroupCommitWal = true,
            WalDurabilityMode = DurabilityMode.FullSync,
        };

        // Act - Create three instances at same path (should NOT throw)
        var db1 = factory.Create(testDbPath, "pass1", false, config);
        var db2 = factory.Create(testDbPath, "pass2", false, config);
        var db3 = factory.Create(testDbPath, "pass3", false, config);

        // Assert - All instances created successfully
        Assert.NotNull(db1);
        Assert.NotNull(db2);
        Assert.NotNull(db3);

        // Verify each has its own WAL file
        var walFiles = Directory.GetFiles(testDbPath, "wal-*.log");
        Assert.Equal(3, walFiles.Length);

        // Cleanup
        ((Database)db1).Dispose();
        ((Database)db2).Dispose();
        ((Database)db3).Dispose();

        // Verify WAL files cleaned up
        walFiles = Directory.GetFiles(testDbPath, "wal-*.log");
        Assert.Empty(walFiles);
    }

    /// <summary>
    /// Test 2: Concurrent writes from multiple instances.
    /// Verifies true concurrent access without serialization.
    /// NOTE: Uses separate database paths since concurrent access to same metadata is not supported.
    /// </summary>
    [Fact]
    public async Task ConcurrentWrites_MultipleInstances_Success()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<DatabaseFactory>();

        var config = new DatabaseConfig
        {
            UseGroupCommitWal = true,
            WalDurabilityMode = DurabilityMode.Async,  // Fast mode for this test
            WalMaxBatchSize = 10,
        };

        const int instanceCount = 8;
        const int writesPerInstance = 100;

        // Act - Multiple instances writing concurrently (each to its own database path)
        var tasks = new List<Task>();
        for (int i = 0; i < instanceCount; i++)
        {
            int instanceId = i;
            tasks.Add(Task.Run(() =>
            {
                // Use separate database path for each instance
                var instanceDbPath = Path.Combine(testDbPath, $"instance{instanceId}");
                Directory.CreateDirectory(instanceDbPath);
                
                var db = (Database)factory.Create(instanceDbPath, $"pass{instanceId}", false, config);
                try
                {
                    db.ExecuteSQL("CREATE TABLE test (id INTEGER, value TEXT)");

                    for (int j = 0; j < writesPerInstance; j++)
                    {
                        int id = instanceId * 1000 + j;
                        db.ExecuteSQL($"INSERT INTO test VALUES ({id}, 'Instance{instanceId}_Row{j}')");
                    }
                }
                finally
                {
                    db.Dispose();
                }
            }));
        }

        // Wait for all instances to complete
        await Task.WhenAll(tasks);

        // Assert - All writes succeeded (no IOException)
        // Verify each instance's WAL was cleaned up
        for (int i = 0; i < instanceCount; i++)
        {
            var instanceDbPath = Path.Combine(testDbPath, $"instance{i}");
            var walFiles = Directory.GetFiles(instanceDbPath, "wal-*.log");
            Assert.Empty(walFiles);  // All cleaned up
        }
    }

    /// <summary>
    /// Test 3: WAL file cleanup on dispose.
    /// Verifies instance-specific WAL files are deleted.
    /// </summary>
    [Fact]
    public void Dispose_CleansUpWALFile()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<DatabaseFactory>();

        var config = new DatabaseConfig { UseGroupCommitWal = true };

        // Act
        var db = (Database)factory.Create(testDbPath, "password", false, config);
        
        // Verify WAL file exists
        var walFiles = Directory.GetFiles(testDbPath, "wal-*.log");
        Assert.Single(walFiles);

        var walFilePath = walFiles[0];
        Assert.True(File.Exists(walFilePath));

        // Dispose
        db.Dispose();

        // Assert - WAL file deleted
        Assert.False(File.Exists(walFilePath));
        walFiles = Directory.GetFiles(testDbPath, "wal-*.log");
        Assert.Empty(walFiles);
    }

    /// <summary>
    /// Test 4: Orphaned WAL cleanup.
    /// Simulates a crash by leaving WAL files behind.
    /// </summary>
    [Fact]
    public void CleanupOrphanedWAL_RemovesOldFiles()
    {
        // Arrange - Create fake old WAL files
        var oldWalPath = Path.Combine(testDbPath, "wal-old123abc.log");
        File.WriteAllText(oldWalPath, "fake wal data");
        
        // Set last write time to 2 hours ago
        File.SetLastWriteTime(oldWalPath, DateTime.Now.AddHours(-2));

        // Act - Cleanup orphaned files
        int deletedCount = GroupCommitWAL.CleanupOrphanedWAL(testDbPath, TimeSpan.FromHours(1));

        // Assert
        Assert.Equal(1, deletedCount);
        Assert.False(File.Exists(oldWalPath));
    }

    /// <summary>
    /// Test 5: Each instance has unique ID.
    /// Verifies instance IDs are different for isolation.
    /// </summary>
    [Fact]
    public void MultipleInstances_HaveUniqueWALFiles()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<DatabaseFactory>();

        var config = new DatabaseConfig { UseGroupCommitWal = true };

        // Act - Create multiple instances
        var db1 = (Database)factory.Create(testDbPath, "pass", false, config);
        var db2 = (Database)factory.Create(testDbPath, "pass", false, config);
        var db3 = (Database)factory.Create(testDbPath, "pass", false, config);

        // Get WAL files
        var walFiles = Directory.GetFiles(testDbPath, "wal-*.log");
        
        // Assert - Three unique WAL files
        Assert.Equal(3, walFiles.Length);
        
        // Verify all filenames are different
        var filenames = walFiles.Select(Path.GetFileName).ToHashSet();
        Assert.Equal(3, filenames.Count);

        // Cleanup
        db1.Dispose();
        db2.Dispose();
        db3.Dispose();
    }

    /// <summary>
    /// Cleanup method for test resources.
    /// </summary>
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(testDbPath))
            {
                Directory.Delete(testDbPath, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
