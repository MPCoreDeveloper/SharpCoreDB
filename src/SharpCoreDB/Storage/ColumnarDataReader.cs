// <copyright file="ColumnarDataReader.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage;

using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Reads records from columnar storage format (.dat files) for migration purposes.
/// Provides sequential, batch-oriented access to all records in a table.
/// </summary>
public class ColumnarDataReader : IDisposable
{
    private readonly ITable table;
    private readonly IStorage storage;
    private readonly string dataFilePath;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ColumnarDataReader"/> class.
    /// </summary>
    /// <param name="table">The table to read from.</param>
    /// <param name="storage">The storage instance for reading encrypted data.</param>
    public ColumnarDataReader(ITable table, IStorage storage)
    {
        this.table = table ?? throw new ArgumentNullException(nameof(table));
        this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
        this.dataFilePath = table.DataFile;

        if (!File.Exists(dataFilePath))
        {
            throw new FileNotFoundException($"Data file not found: {dataFilePath}");
        }
    }

    /// <summary>
    /// Reads all records from the columnar data file.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of all records as dictionaries.</returns>
    public async Task<List<Dictionary<string, object>>> ReadAllRecordsAsync(CancellationToken cancellationToken = default)
    {
        var records = new List<Dictionary<string, object>>();
        var fileInfo = new FileInfo(dataFilePath);

        if (fileInfo.Length == 0)
        {
            return records; // Empty file
        }

        long position = 0;
        long fileLength = fileInfo.Length;

        while (position < fileLength)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Read record data from file
            var rowData = await Task.Run(() => storage.ReadBytesFrom(dataFilePath, position), cancellationToken);
            
            if (rowData == null || rowData.Length == 0)
            {
                break; // End of file or corrupted data
            }

            // Deserialize record
            var row = DeserializeRecord(rowData);
            
            if (row != null && row.Count > 0)
            {
                records.Add(row);
            }

            // Move to next record (4-byte length prefix + data)
            position += 4 + rowData.Length;
        }

        return records;
    }

    /// <summary>
    /// Reads records in batches for memory-efficient processing of large tables.
    /// </summary>
    /// <param name="batchSize">Number of records per batch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable of record batches.</returns>
    public async IAsyncEnumerable<List<Dictionary<string, object>>> ReadBatchesAsync(
        int batchSize = 1000,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var fileInfo = new FileInfo(dataFilePath);
        
        if (fileInfo.Length == 0)
        {
            yield break;
        }

        long position = 0;
        long fileLength = fileInfo.Length;
        var currentBatch = new List<Dictionary<string, object>>(batchSize);

        while (position < fileLength)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Read record data
            var rowData = await Task.Run(() => storage.ReadBytesFrom(dataFilePath, position), cancellationToken);
            
            if (rowData == null || rowData.Length == 0)
            {
                break;
            }

            // Deserialize record
            var row = DeserializeRecord(rowData);
            
            if (row != null && row.Count > 0)
            {
                currentBatch.Add(row);

                // Yield batch when full
                if (currentBatch.Count >= batchSize)
                {
                    yield return new List<Dictionary<string, object>>(currentBatch);
                    currentBatch.Clear();
                }
            }

            // Move to next record
            position += 4 + rowData.Length;
        }

        // Yield remaining records
        if (currentBatch.Count > 0)
        {
            yield return currentBatch;
        }
    }

    /// <summary>
    /// Counts the total number of records in the data file.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Total record count.</returns>
    public async Task<long> CountRecordsAsync(CancellationToken cancellationToken = default)
    {
        var fileInfo = new FileInfo(dataFilePath);
        
        if (fileInfo.Length == 0)
        {
            return 0;
        }

        long count = 0;
        long position = 0;
        long fileLength = fileInfo.Length;

        while (position < fileLength)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Read just the length prefix (4 bytes)
            var lengthBuffer = new byte[4];
            await using var fs = new FileStream(dataFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            fs.Seek(position, SeekOrigin.Begin);
            int bytesRead = await fs.ReadAsync(lengthBuffer.AsMemory(), cancellationToken);

            if (bytesRead < 4)
            {
                break; // End of file
            }

            int recordLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);
            
            if (recordLength <= 0 || recordLength > 100_000_000) // Sanity check
            {
                break; // Corrupted data
            }

            count++;
            position += 4 + recordLength;
        }

        return count;
    }

    /// <summary>
    /// Gets the file size in bytes.
    /// </summary>
    public long GetFileSizeBytes()
    {
        var fileInfo = new FileInfo(dataFilePath);
        return fileInfo.Length;
    }

    /// <summary>
    /// Computes a checksum of all data for verification.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>CRC32 checksum of all records.</returns>
    public async Task<uint> ComputeChecksumAsync(CancellationToken cancellationToken = default)
    {
        uint crc = 0xFFFFFFFF;
        var fileInfo = new FileInfo(dataFilePath);

        if (fileInfo.Length == 0)
        {
            return ~crc;
        }

        await using var fs = new FileStream(dataFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var buffer = new byte[8192]; // 8KB buffer

        int bytesRead;
        while ((bytesRead = await fs.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            crc = Services.Crc32.Compute(buffer.AsSpan(0, bytesRead));
        }

        return ~crc;
    }

    /// <summary>
    /// Deserializes a record from binary data to a dictionary.
    /// </summary>
    private Dictionary<string, object> DeserializeRecord(byte[] rowData)
    {
        var row = new Dictionary<string, object>();
        var span = rowData.AsSpan();
        int offset = 0;

        for (int i = 0; i < table.Columns.Count; i++)
        {
            if (offset >= span.Length)
            {
                break; // Incomplete record
            }

            try
            {
                var columnName = table.Columns[i];
                var columnType = table.ColumnTypes[i];

                // Read typed value from span
                var value = ReadTypedValue(span.Slice(offset), columnType, out int bytesRead);
                row[columnName] = value;
                offset += bytesRead;
            }
            catch
            {
                // Skip corrupted column
                break;
            }
        }

        return row;
    }

    /// <summary>
    /// Reads a typed value from a span.
    /// </summary>
    private static object ReadTypedValue(ReadOnlySpan<byte> span, DataType dataType, out int bytesRead)
    {
        bytesRead = 0;

        switch (dataType)
        {
            case DataType.Integer:
                if (span.Length < 4)
                    return 0;
                bytesRead = 4;
                return BinaryPrimitives.ReadInt32LittleEndian(span);

            case DataType.Long:
                if (span.Length < 8)
                    return 0L;
                bytesRead = 8;
                return BinaryPrimitives.ReadInt64LittleEndian(span);

            case DataType.Real:
                if (span.Length < 8)
                    return 0.0;
                bytesRead = 8;
                return BinaryPrimitives.ReadDoubleLittleEndian(span);

            case DataType.Decimal:
                if (span.Length < 16)
                    return 0m;
                bytesRead = 16;
                int[] bits = new int[4];
                for (int i = 0; i < 4; i++)
                {
                    bits[i] = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(i * 4, 4));
                }
                return new decimal(bits);

            case DataType.Boolean:
                if (span.Length < 1)
                    return false;
                bytesRead = 1;
                return span[0] != 0;

            case DataType.DateTime:
                if (span.Length < 8)
                    return DateTime.MinValue;
                bytesRead = 8;
                long binaryValue = BinaryPrimitives.ReadInt64LittleEndian(span);
                return DateTime.FromBinary(binaryValue);

            case DataType.String:
            case DataType.Ulid:
            case DataType.Guid:
                if (span.Length < 4)
                    return string.Empty;
                
                int length = BinaryPrimitives.ReadInt32LittleEndian(span);
                if (length < 0 || length > span.Length - 4)
                    return string.Empty;

                bytesRead = 4 + length;
                return System.Text.Encoding.UTF8.GetString(span.Slice(4, length));

            case DataType.Blob:
                if (span.Length < 4)
                    return Array.Empty<byte>();
                
                int blobLength = BinaryPrimitives.ReadInt32LittleEndian(span);
                if (blobLength < 0 || blobLength > span.Length - 4)
                    return Array.Empty<byte>();

                bytesRead = 4 + blobLength;
                return span.Slice(4, blobLength).ToArray();

            default:
                return string.Empty;
        }
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
                // No managed resources to dispose currently
            }

            disposed = true;
        }
    }
}
