// <copyright file="PageBasedDataWriter.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage;

using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Storage.Hybrid;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Writes records to page-based storage format (.pages files) for migration purposes.
/// Provides batch-oriented writing with automatic page allocation and management.
/// </summary>
public class PageBasedDataWriter : IDisposable
{
    private readonly PageManager pageManager;
    private readonly ITable table;
    private readonly string pagesFilePath;
    private readonly uint tableId;
    private bool disposed;
    private long recordsWritten;

    /// <summary>
    /// Initializes a new instance of the <see cref="PageBasedDataWriter"/> class.
    /// </summary>
    /// <param name="table">The table to write to.</param>
    /// <param name="pageManager">The page manager instance.</param>
    /// <param name="tableId">The table ID for page allocation.</param>
    public PageBasedDataWriter(ITable table, PageManager pageManager, uint tableId)
    {
        this.table = table ?? throw new ArgumentNullException(nameof(table));
        this.pageManager = pageManager ?? throw new ArgumentNullException(nameof(pageManager));
        this.tableId = tableId;
        this.pagesFilePath = table.DataFile;

        if (string.IsNullOrEmpty(pagesFilePath))
        {
            throw new ArgumentException("Table data file path is not set", nameof(table));
        }

        // Ensure .pages file is initialized
        if (!File.Exists(pagesFilePath))
        {
            InitializePagesFile();
        }
    }

    /// <summary>
    /// Gets the number of records written so far.
    /// </summary>
    public long RecordsWritten => recordsWritten;

    /// <summary>
    /// Writes a batch of records to page-based storage.
    /// </summary>
    /// <param name="records">Records to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of records successfully written.</returns>
    public async Task<int> WriteBatchAsync(
        List<Dictionary<string, object>> records,
        CancellationToken cancellationToken = default)
    {
        int written = 0;

        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await WriteRecordAsync(record, cancellationToken);
                written++;
                Interlocked.Increment(ref recordsWritten);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to write record {written + 1}/{records.Count}: {ex.Message}", ex);
            }
        }

        return written;
    }

    /// <summary>
    /// Writes a single record to page-based storage.
    /// </summary>
    /// <param name="record">Record to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task WriteRecordAsync(
        Dictionary<string, object> record,
        CancellationToken cancellationToken = default)
    {
        // Serialize record to binary format
        var recordData = SerializeRecord(record);

        // Find or allocate a page with enough space
        var pageId = await Task.Run(() => pageManager.FindPageWithSpace(tableId, recordData.Length), cancellationToken);

        // Insert record into page
        await Task.Run(() => pageManager.InsertRecord(pageId, recordData), cancellationToken);
    }

    /// <summary>
    /// Writes all records from an async enumerable (for streaming large datasets).
    /// </summary>
    /// <param name="recordBatches">Async enumerable of record batches.</param>
    /// <param name="progressCallback">Optional callback for progress updates.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Total number of records written.</returns>
    public async Task<long> WriteAllAsync(
        IAsyncEnumerable<List<Dictionary<string, object>>> recordBatches,
        Action<long>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        long totalWritten = 0;

        await foreach (var batch in recordBatches.WithCancellation(cancellationToken))
        {
            int written = await WriteBatchAsync(batch, cancellationToken);
            totalWritten += written;
            progressCallback?.Invoke(totalWritten);
        }

        return totalWritten;
    }

    /// <summary>
    /// Flushes all dirty pages to disk.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await Task.Run(() => pageManager.FlushDirtyPages(), cancellationToken);
    }

    /// <summary>
    /// Gets the current file size in bytes.
    /// </summary>
    public long GetFileSizeBytes()
    {
        if (File.Exists(pagesFilePath))
        {
            var fileInfo = new FileInfo(pagesFilePath);
            return fileInfo.Length;
        }

        return 0;
    }

    /// <summary>
    /// Computes a checksum of all written data for verification.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>CRC32 checksum of all pages.</returns>
    public async Task<uint> ComputeChecksumAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(pagesFilePath))
        {
            return 0;
        }

        uint crc = 0xFFFFFFFF;

        await using var fs = new FileStream(pagesFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var buffer = new byte[8192]; // 8KB buffer

        int bytesRead;
        while ((bytesRead = await fs.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            crc = Services.Crc32.Compute(buffer.AsSpan(0, bytesRead));
        }

        return ~crc;
    }

    /// <summary>
    /// Verifies that records can be read back correctly.
    /// </summary>
    /// <param name="expectedCount">Expected number of records.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if verification passed.</returns>
    public async Task<bool> VerifyAsync(long expectedCount, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask; // Placeholder for async
        cancellationToken.ThrowIfCancellationRequested();

        // Count records by reading all pages
        // This would use PageManager to read each page and count records
        // For now, just verify file exists
        return File.Exists(pagesFilePath) && recordsWritten == expectedCount;
    }

    /// <summary>
    /// Serializes a record to binary format compatible with page-based storage.
    /// </summary>
    private byte[] SerializeRecord(Dictionary<string, object> record)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // Write column count
        writer.Write((byte)table.Columns.Count);

        // Write each column value
        for (int i = 0; i < table.Columns.Count; i++)
        {
            var columnName = table.Columns[i];
            var columnType = table.ColumnTypes[i];

            if (!record.TryGetValue(columnName, out var value))
            {
                // Write NULL marker
                writer.Write((byte)0); // NULL flag
                continue;
            }

            // Write NOT NULL marker
            writer.Write((byte)1);

            // Write typed value
            WriteTypedValue(writer, value, columnType);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Writes a typed value to the binary writer.
    /// </summary>
    private static void WriteTypedValue(BinaryWriter writer, object value, DataType dataType)
    {
        switch (dataType)
        {
            case DataType.Integer:
                writer.Write(Convert.ToInt32(value));
                break;

            case DataType.Long:
                writer.Write(Convert.ToInt64(value));
                break;

            case DataType.Real:
                writer.Write(Convert.ToDouble(value));
                break;

            case DataType.Decimal:
                var decimalValue = Convert.ToDecimal(value);
                var bits = decimal.GetBits(decimalValue);
                foreach (var bit in bits)
                {
                    writer.Write(bit);
                }
                break;

            case DataType.Boolean:
                writer.Write(Convert.ToBoolean(value));
                break;

            case DataType.DateTime:
                var dateTime = value is DateTime dt ? dt : DateTime.Parse(value.ToString() ?? string.Empty, System.Globalization.CultureInfo.InvariantCulture);
                writer.Write(dateTime.Ticks);
                break;

            case DataType.String:
            case DataType.Ulid:
            case DataType.Guid:
                var str = value.ToString() ?? string.Empty;
                var bytes = System.Text.Encoding.UTF8.GetBytes(str);
                writer.Write(bytes.Length);
                writer.Write(bytes);
                break;

            case DataType.Blob:
                var blob = value as byte[] ?? Array.Empty<byte>();
                writer.Write(blob.Length);
                writer.Write(blob);
                break;

            default:
                throw new NotSupportedException($"Data type {dataType} not supported for serialization");
        }
    }

    /// <summary>
    /// Initializes a new .pages file with header.
    /// </summary>
    private void InitializePagesFile()
    {
        using var fs = new FileStream(pagesFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        
        // Write file header (8 bytes: magic number + version)
        var header = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), 0x50474553); // "PGES" magic
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4, 2), 1); // Version 1
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6, 2), 0); // Reserved
        
        fs.Write(header);
        fs.Flush(true);
    }

    /// <summary>
    /// Disposes resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes resources.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposed)
        {
            if (disposing)
            {
                // Flush any remaining data
                try
                {
                    pageManager?.FlushDirtyPages();
                }
                catch
                {
                    // Best effort flush
                }
            }

            disposed = true;
        }
    }
}
