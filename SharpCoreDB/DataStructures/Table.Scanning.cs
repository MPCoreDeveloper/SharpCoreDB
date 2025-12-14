namespace SharpCoreDB.DataStructures;

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using SharpCoreDB.Services;

/// <summary>
/// Query scanning methods for Table - SIMD-accelerated row scanning.
/// </summary>
public partial class Table
{
    /// <summary>
    /// SIMD-accelerated row scanning for full table scans.
    /// FIXED: Now correctly handles length-prefixed records written by AppendBytes.
    /// Previously used legacy BinaryReader which didn't handle length prefixes.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private List<Dictionary<string, object>> ScanRowsWithSimd(byte[] data, string? where)
    {
        var results = new List<Dictionary<string, object>>();
        
        // CRITICAL FIX: Use same length-prefixed reading logic as ReadRowAtPosition
        // Storage.AppendBytes writes: [4-byte length][data]
        // So we need to read length prefix, then data, then repeat
        
        int filePosition = 0;
        ReadOnlySpan<byte> dataSpan = data.AsSpan();
        
        while (filePosition < dataSpan.Length)
        {
            // Read length prefix (4 bytes)
            if (filePosition + 4 > dataSpan.Length)
            {
                // Not enough bytes for length prefix - end of file
                break;
            }
            
            int recordLength = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(
                dataSpan.Slice(filePosition, 4));
            
            // Sanity check: record length must be reasonable
            const int MaxRecordSize = 1_000_000_000; // 1 GB max per record
            if (recordLength < 0 || recordLength > MaxRecordSize)
            {
                // Invalid length - likely corrupt data, stop scanning
                Console.WriteLine($"⚠️  ScanRowsWithSimd: Invalid record length {recordLength} at position {filePosition}");
                break;
            }
            
            if (recordLength == 0)
            {
                // Empty record (all NULL fields) - skip length prefix and continue
                filePosition += 4;
                continue;
            }
            
            // Check if we have enough data for the record
            if (filePosition + 4 + recordLength > dataSpan.Length)
            {
                // Incomplete record - end of file
                Console.WriteLine($"⚠️  ScanRowsWithSimd: Incomplete record at position {filePosition}: need {recordLength} bytes, have {dataSpan.Length - filePosition - 4}");
                break;
            }
            
            // Skip length prefix (4 bytes) and read record data
            int dataOffset = filePosition + 4;
            ReadOnlySpan<byte> recordData = dataSpan.Slice(dataOffset, recordLength);
            
            // Parse the record into a row
            var row = new Dictionary<string, object>();
            bool valid = true;
            int offset = 0;
            
            for (int i = 0; i < this.Columns.Count; i++)
            {
                try
                {
                    var value = ReadTypedValueFromSpan(recordData.Slice(offset), this.ColumnTypes[i], out int bytesRead);
                    row[this.Columns[i]] = value;
                    offset += bytesRead;
                }
                catch
                {
                    valid = false;
                    break;
                }
            }
            
            // Add row to results if valid and matches WHERE clause
            if (valid && (string.IsNullOrEmpty(where) || EvaluateWhere(row, where)))
            {
                results.Add(row);
            }
            
            // Move to next record (skip length prefix + data)
            filePosition += 4 + recordLength;
        }
        
        return results;
    }

    /// <summary>
    /// Compares two rows for equality using SIMD-accelerated byte comparison.
    /// Used for duplicate detection and WHERE clause evaluation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private bool CompareRows(Dictionary<string, object> row1, Dictionary<string, object> row2)
    {
        if (row1.Count != row2.Count)
            return false;

        foreach (var kvp in row1)
        {
            if (!row2.TryGetValue(kvp.Key, out var value2))
                return false;

            // SIMD: Use vectorized comparison for byte arrays
            if (kvp.Value is byte[] bytes1 && value2 is byte[] bytes2)
            {
                if (!SimdHelper.SequenceEqual(bytes1, bytes2))
                    return false;
            }
            else if (!Equals(kvp.Value, value2))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Searches for a pattern in serialized row data using SIMD acceleration.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private int FindPatternInRowData(ReadOnlySpan<byte> rowData, byte pattern)
    {
        return SimdHelper.IndexOf(rowData, pattern);
    }

    private static bool EvaluateWhere(Dictionary<string, object> row, string? where)
    {
        if (string.IsNullOrEmpty(where)) return true;
        var parts = where.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 3 && parts[1] == "=")
            return row.TryGetValue(parts[0], out var val) && val?.ToString() == parts[2].Trim('\'');
        return true;
    }
}
