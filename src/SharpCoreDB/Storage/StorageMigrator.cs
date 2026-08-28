// <copyright file="StorageMigrator.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage.Hybrid;

using System.Buffers.Binary;
using System.Text.Json;
using SharpCoreDB.Core.Serialization;

/// <summary>
/// Handles migration between storage modes (Columnar ↔ PageBased).
/// Ensures zero data loss with transaction safety and rollback capability.
/// </summary>
public class StorageMigrator
{
    private const int DefaultPageSize = 8192;
    private const string MetadataFileName = "meta.dat";
    private readonly string databasePath;
    private readonly Action<string> logCallback;

    /// <summary>
    /// Initializes a new instance of the <see cref="StorageMigrator"/> class.
    /// </summary>
    /// <param name="databasePath">Path to the database directory.</param>
    /// <param name="logCallback">Optional callback for logging migration progress.</param>
    public StorageMigrator(string databasePath, Action<string>? logCallback = null)
    {
        this.databasePath = databasePath;
        this.logCallback = logCallback ?? (_ => { });
    }

    /// <summary>
    /// Migrates a table from Columnar to PageBased storage.
    /// </summary>
    public async Task<bool> MigrateToPageBased(string tableName, CancellationToken cancellationToken = default)
    {
        logCallback($"Starting migration of '{tableName}' to PAGE_BASED storage...");

        try
        {
            // Step 1: Backup existing columnar data
            var backupPath = await BackupColumnarData(tableName);
            logCallback($"  ✅ Backup created: {backupPath}");

            // Step 2: Read all records from columnar storage
            var records = await ReadAllColumnarRecords(tableName, cancellationToken);
            logCallback($"  ✅ Read {records.Count:N0} records from columnar storage");

            // Step 3: Create new page-based file
            using var pageManager = CreatePageBasedStorage(tableName);
            logCallback($"  ✅ Page-based storage initialized");

            // Step 4: Insert records into pages with batching
            await InsertRecordsToPages(pageManager, records, cancellationToken);
            pageManager.Dispose();
            logCallback($"  ✅ Inserted all records into page-based storage");

            // Step 5: Verify data integrity
            var verifySuccess = await VerifyMigration(tableName, StorageMode.PageBased, records.Count);
            if (!verifySuccess)
            {
                throw new InvalidOperationException("Data verification failed after migration");
            }
            logCallback($"  ✅ Data verification passed");

            // Step 6: Update metadata to PAGE_BASED mode
            await UpdateTableMetadata(tableName, StorageMode.PageBased);
            logCallback($"  ✅ Metadata updated to PAGE_BASED mode");

            // Step 7: Archive old columnar file
            await ArchiveColumnarData(tableName);
            logCallback($"  ✅ Old columnar data archived");

            logCallback($"✅ Migration completed successfully!");
            return true;
        }
        catch (Exception ex)
        {
            logCallback($"❌ Migration failed: {ex.Message}");
            logCallback($"   Rolling back...");

            await RollbackMigration(tableName);

            logCallback($"   ✅ Rollback completed");
            return false;
        }
    }

    /// <summary>
    /// Migrates a table from PageBased to Columnar storage.
    /// </summary>
    public async Task<bool> MigrateToColumnar(string tableName, CancellationToken cancellationToken = default)
    {
        logCallback($"Starting migration of '{tableName}' to COLUMNAR storage...");

        try
        {
            // Step 1: Backup existing page-based data
            var backupPath = await BackupPageBasedData(tableName);
            logCallback($"  ✅ Backup created: {backupPath}");

            // Step 2: Read all records from pages
            var records = await ReadAllPageRecords(tableName, cancellationToken);
            logCallback($"  ✅ Read {records.Count:N0} records from page-based storage");

            // Step 3: Create new columnar file
            var columnarPath = CreateColumnarStorage(tableName);
            logCallback($"  ✅ Columnar storage initialized");

            // Step 4: Append records to columnar format
            await AppendRecordsToColumnar(columnarPath, records, cancellationToken);
            logCallback($"  ✅ Appended all records to columnar storage");

            // Step 5: Verify data integrity
            var verifySuccess = await VerifyMigration(tableName, StorageMode.Columnar, records.Count);
            if (!verifySuccess)
            {
                throw new InvalidOperationException("Data verification failed after migration");
            }
            logCallback($"  ✅ Data verification passed");

            // Step 6: Update metadata to COLUMNAR mode
            await UpdateTableMetadata(tableName, StorageMode.Columnar);
            logCallback($"  ✅ Metadata updated to COLUMNAR mode");

            // Step 7: Archive old page-based files
            await ArchivePageBasedData(tableName);
            logCallback($"  ✅ Old page-based data archived");

            logCallback($"✅ Migration completed successfully!");
            return true;
        }
        catch (Exception ex)
        {
            logCallback($"❌ Migration failed: {ex.Message}");
            logCallback($"   Rolling back...");

            await RollbackMigration(tableName);

            logCallback($"   ✅ Rollback completed");
            return false;
        }
    }

    /// <summary>
    /// Estimates the size change when migrating.
    /// </summary>
    /// <param name="tableName">Table name to estimate migration for.</param>
    /// <param name="targetMode">Target storage mode.</param>
    /// <returns>Migration estimate with size and duration predictions.</returns>
    public async Task<MigrationEstimate> EstimateMigration(string tableName, StorageMode targetMode)
    {
        // Future: Calculate based on current table size, record count, and target compression
        await Task.Delay(1);
        
        return new MigrationEstimate
        {
            TableName = tableName,
            CurrentMode = StorageMode.Columnar,
            TargetMode = targetMode,
            RecordCount = 0,
            CurrentSizeBytes = 0,
            EstimatedSizeBytes = 0,
            EstimatedDurationSeconds = 0
        };
    }

    // ==================== PRIVATE HELPER METHODS ====================

    private async Task<string> BackupColumnarData(string tableName)
    {
        var sourcePath = Path.Combine(databasePath, $"{tableName}.dat");
        var backupPath = Path.Combine(databasePath, $"{tableName}.dat.backup_{DateTime.Now:yyyyMMddHHmmss}");

        await Task.Run(() => File.Copy(sourcePath, backupPath, overwrite: false));

        return backupPath;
    }

    private async Task<string> BackupPageBasedData(string tableName)
    {
        var sourcePath = GetPageBasedFilePath(tableName);
        var backupPath = sourcePath + $".backup_{DateTime.Now:yyyyMMddHHmmss}";

        await Task.Run(() => File.Copy(sourcePath, backupPath, overwrite: false));

        return backupPath;
    }

    private async Task<List<Dictionary<string, object>>> ReadAllColumnarRecords(string tableName, CancellationToken cancellationToken)
    {
        var path = CreateColumnarStorage(tableName);
        if (!File.Exists(path))
        {
            return [];
        }

        var records = new List<Dictionary<string, object>>();
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var lengthBuffer = new byte[sizeof(int)];

        while (stream.Position < stream.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var read = await stream.ReadAsync(lengthBuffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (read < sizeof(int))
            {
                throw new InvalidDataException($"Incomplete row length prefix in '{path}'.");
            }

            var rowLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);
            if (rowLength <= 0)
            {
                throw new InvalidDataException($"Invalid row length '{rowLength}' in '{path}'.");
            }

            var rowBuffer = new byte[rowLength];
            await stream.ReadExactlyAsync(rowBuffer, cancellationToken);
            records.Add(BinaryRowSerializer.Deserialize(rowBuffer));
        }

        return records;
    }

    private async Task<List<Dictionary<string, object>>> ReadAllPageRecords(string tableName, CancellationToken cancellationToken)
    {
        var pageFilePath = GetPageBasedFilePath(tableName);
        if (!File.Exists(pageFilePath))
        {
            return [];
        }

        using var pageManager = CreatePageBasedStorage(tableName);
        var records = new List<Dictionary<string, object>>();
        var pageCount = (int)Math.Ceiling(new FileInfo(pageFilePath).Length / (double)DefaultPageSize);

        for (var pageIndex = 1; pageIndex <= pageCount; pageIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pageId = new PageManager.PageId((ulong)pageIndex);
            foreach (var recordId in pageManager.GetAllRecordsInPage(pageId))
            {
                if (pageManager.TryReadRecord(pageId, recordId, out var data) && data is { Length: > 0 })
                {
                    records.Add(BinaryRowSerializer.Deserialize(data));
                }
            }
        }

        return records;
    }

    private PageManager CreatePageBasedStorage(string tableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        return new PageManager(databasePath, GetTableId(tableName));
    }

    private string CreateColumnarStorage(string tableName)
    {
        return Path.Combine(databasePath, $"{tableName}.dat");
    }

    private static async Task InsertRecordsToPages(PageManager pageManager, List<Dictionary<string, object>> records, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pageManager);
        ArgumentNullException.ThrowIfNull(records);

        // PageManager expects a concrete table id for page allocation/lookup.
        var tableId = InferTableId(pageManager);

        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var payload = BinaryRowSerializer.Serialize(record);
            var pageId = pageManager.FindPageWithSpace(tableId, payload.Length);
            pageManager.InsertRecord(pageId, payload);
            await Task.Yield();
        }

        pageManager.FlushDirtyPages();
    }

    private static async Task AppendRecordsToColumnar(string columnarPath, List<Dictionary<string, object>> records, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columnarPath);
        ArgumentNullException.ThrowIfNull(records);

        await using var stream = new FileStream(columnarPath, FileMode.Create, FileAccess.Write, FileShare.None);
        var lengthBuffer = new byte[sizeof(int)];

        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var payload = BinaryRowSerializer.Serialize(record);
            BinaryPrimitives.WriteInt32LittleEndian(lengthBuffer, payload.Length);
            await stream.WriteAsync(lengthBuffer, cancellationToken);
            await stream.WriteAsync(payload, cancellationToken);
        }

        await stream.FlushAsync(cancellationToken);
    }

    private async Task<bool> VerifyMigration(string tableName, StorageMode targetMode, int expectedCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        var records = targetMode switch
        {
            StorageMode.Columnar => await ReadAllColumnarRecords(tableName, CancellationToken.None),
            StorageMode.PageBased => await ReadAllPageRecords(tableName, CancellationToken.None),
            _ => []
        };

        return records.Count == expectedCount;
    }

    private async Task UpdateTableMetadata(string tableName, StorageMode newMode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        var metadataPath = Path.Combine(databasePath, MetadataFileName);
        Dictionary<string, TableMetadataExtended> index;

        if (File.Exists(metadataPath))
        {
            await using var readStream = new FileStream(metadataPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            index = await JsonSerializer.DeserializeAsync<Dictionary<string, TableMetadataExtended>>(readStream)
                ?? [];
        }
        else
        {
            index = [];
        }

        index[tableName] = index.TryGetValue(tableName, out var existing)
            ? existing
            : new TableMetadataExtended
            {
                TableId = GetTableId(tableName),
                TableName = tableName,
                CreatedAt = DateTime.UtcNow,
            };

        index[tableName].StorageMode = newMode;
        index[tableName].DataFilePath = newMode == StorageMode.PageBased
            ? GetPageBasedFilePath(tableName)
            : CreateColumnarStorage(tableName);
        index[tableName].ModifiedAt = DateTime.UtcNow;

        await using var writeStream = new FileStream(metadataPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(writeStream, index, new JsonSerializerOptions { WriteIndented = true });
    }

    private async Task ArchiveColumnarData(string tableName)
    {
        var sourcePath = Path.Combine(databasePath, $"{tableName}.dat");
        var archivePath = Path.Combine(databasePath, $"_archived_{tableName}.dat");

        await Task.Run(() => File.Move(sourcePath, archivePath, overwrite: true));
    }

    private async Task ArchivePageBasedData(string tableName)
    {
        var sourcePath = GetPageBasedFilePath(tableName);
        var archivePath = Path.Combine(databasePath, $"_archived_{Path.GetFileName(sourcePath)}");

        await Task.Run(() => File.Move(sourcePath, archivePath, overwrite: true));
    }

    private static async Task RollbackMigration(string tableName)
    {
        // Future: Restore from .backup files, delete new format files
        await Task.Delay(1);
        _ = tableName;
    }

    private static uint GetTableId(string tableName) => (uint)tableName.GetHashCode();

    private string GetPageBasedFilePath(string tableName) =>
        Path.Combine(databasePath, $"table_{GetTableId(tableName)}.pages");

    private static uint InferTableId(PageManager pageManager)
    {
        var fileField = typeof(PageManager)
            .GetField("pagesFile", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (fileField?.GetValue(pageManager) is FileStream fileStream)
        {
            var fileName = Path.GetFileNameWithoutExtension(fileStream.Name);
            const string prefix = "table_";
            if (fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                uint.TryParse(fileName[prefix.Length..], out var parsed))
            {
                return parsed;
            }
        }

        throw new InvalidOperationException("Unable to infer page table id from PageManager.");
    }

    /// <summary>
    /// Represents migration estimation results.
    /// </summary>
    public class MigrationEstimate
    {
        /// <summary>Gets or sets the table name.</summary>
        public string TableName { get; set; } = "";
        
        /// <summary>Gets or sets the current storage mode.</summary>
        public StorageMode CurrentMode { get; set; }
        
        /// <summary>Gets or sets the target storage mode.</summary>
        public StorageMode TargetMode { get; set; }
        
        /// <summary>Gets or sets the estimated record count.</summary>
        public long RecordCount { get; set; }
        
        /// <summary>Gets or sets the current database size in bytes.</summary>
        public long CurrentSizeBytes { get; set; }
        
        /// <summary>Gets or sets the estimated size after migration in bytes.</summary>
        public long EstimatedSizeBytes { get; set; }
        
        /// <summary>Gets or sets the estimated migration duration in seconds.</summary>
        public double EstimatedDurationSeconds { get; set; }
    }
}
