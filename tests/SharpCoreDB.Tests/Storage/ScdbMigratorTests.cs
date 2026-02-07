// <copyright file="ScdbMigratorTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests.Storage;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SharpCoreDB.Storage;
using SharpCoreDB.Storage.Scdb;
using MigrationProgress = SharpCoreDB.Storage.Scdb.MigrationProgress;
using Xunit;

/// <summary>
/// Tests for ScdbMigrator.
/// ✅ SCDB Phase 4: Verifies migration from Directory to SCDB format.
/// </summary>
public sealed class ScdbMigratorTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _sourceDir;
    private readonly string _targetPath;

    public ScdbMigratorTests(ITestOutputHelper output)
    {
        _output = output;
        _sourceDir = Path.Combine(Path.GetTempPath(), $"migrator_source_{Guid.NewGuid():N}");
        _targetPath = Path.Combine(Path.GetTempPath(), $"migrator_target_{Guid.NewGuid():N}.scdb");
        
        // Create source directory
        Directory.CreateDirectory(_sourceDir);
    }

    public void Dispose()
    {
        CleanupTestFiles();
    }
    
    private void CleanupTestFiles()
    {
        try
        {
            if (Directory.Exists(_sourceDir))
            {
                Directory.Delete(_sourceDir, recursive: true);
            }
            
            if (File.Exists(_targetPath))
            {
                File.Delete(_targetPath);
            }
            
            // Clean up backup directories
            var backupDirs = Directory.GetDirectories(Path.GetTempPath(), $"migrator_source_*_backup_*");
            foreach (var dir in backupDirs)
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public async Task Migrate_EmptyDatabase_Success()
    {
        // Arrange
        using var migrator = new ScdbMigrator(_sourceDir, _targetPath);

        // Act
        var result = await migrator.MigrateAsync(new MigrationOptions { CreateBackup = false });

        // Assert
        _output.WriteLine(result.ToString());
        Assert.True(result.Success);
        Assert.Equal(0, result.BlocksMigrated);
    }

    [Fact]
    public async Task Migrate_WithBlocks_AllBlocksMigrated()
    {
        // Arrange - Create some test blocks in source
        var options = new DatabaseOptions { StorageMode = StorageMode.Directory };
        using (var source = DirectoryStorageProvider.Open(_sourceDir, options))
        {
            await source.WriteBlockAsync("block1", new byte[] { 1, 2, 3 });
            await source.WriteBlockAsync("block2", new byte[] { 4, 5, 6, 7, 8 });
            await source.WriteBlockAsync("block3", new byte[] { 9, 10, 11, 12 });
            await source.FlushAsync();
        }
        
        using var migrator = new ScdbMigrator(_sourceDir, _targetPath);

        // Act
        var result = await migrator.MigrateAsync(new MigrationOptions 
        { 
            CreateBackup = false,
            ValidateAfterMigration = true,
        });

        // Assert
        _output.WriteLine(result.ToString());
        Assert.True(result.Success);
        Assert.Equal(3, result.BlocksMigrated);
        Assert.True(result.BytesMigrated > 0);
    }

    [Fact]
    public async Task Migrate_WithProgress_ReportsProgress()
    {
        // Arrange
        var options = new DatabaseOptions { StorageMode = StorageMode.Directory };
        using (var source = DirectoryStorageProvider.Open(_sourceDir, options))
        {
            for (int i = 0; i < 10; i++)
            {
                await source.WriteBlockAsync($"block_{i}", new byte[100]);
            }
            await source.FlushAsync();
        }
        
        var progressReports = new List<MigrationProgress>();
        var progress = new Progress<MigrationProgress>(p => progressReports.Add(p));
        
        using var migrator = new ScdbMigrator(_sourceDir, _targetPath);

        // Act
        var result = await migrator.MigrateAsync(new MigrationOptions 
        { 
            CreateBackup = false,
            Progress = progress,
            BatchSize = 5,
        });

        // Assert
        var localReports = progressReports.ToList();
        _output.WriteLine($"Progress reports: {localReports.Count}");
        foreach (var p in localReports)
        {
            _output.WriteLine($"  {p.Message} - {p.PercentComplete:F1}%");
        }
        
        Assert.True(result.Success);
        Assert.True(localReports.Count > 0);
    }

    [Fact]
    public async Task ValidateSource_ValidDatabase_ReturnsValid()
    {
        // Arrange
        var options = new DatabaseOptions { StorageMode = StorageMode.Directory };
        using (var source = DirectoryStorageProvider.Open(_sourceDir, options))
        {
            await source.WriteBlockAsync("test", new byte[] { 1, 2, 3 });
            await source.FlushAsync();
        }
        
        using var migrator = new ScdbMigrator(_sourceDir, _targetPath);

        // Act
        var result = await migrator.ValidateSourceAsync();

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(1, result.TotalBlocks);
    }

    [Fact]
    public void Constructor_NonexistentSource_ThrowsDirectoryNotFound()
    {
        // Arrange
        var nonexistentDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        // Act & Assert
        Assert.Throws<DirectoryNotFoundException>(() => 
            new ScdbMigrator(nonexistentDir, _targetPath));
    }

    [Fact]
    public async Task Migrate_WithBackup_CreatesBackup()
    {
        // Arrange
        var options = new DatabaseOptions { StorageMode = StorageMode.Directory };
        using (var source = DirectoryStorageProvider.Open(_sourceDir, options))
        {
            await source.WriteBlockAsync("test", new byte[] { 1, 2, 3 });
            await source.FlushAsync();
        }
        
        using var migrator = new ScdbMigrator(_sourceDir, _targetPath);

        // Act
        var result = await migrator.MigrateAsync(new MigrationOptions { CreateBackup = true });

        // Assert
        Assert.True(result.Success);
        
        // Check backup was created
        var backupDirs = Directory.GetDirectories(Path.GetTempPath(), $"migrator_source_*_backup_*");
        Assert.True(backupDirs.Length > 0, "Backup directory should exist");
    }
}
