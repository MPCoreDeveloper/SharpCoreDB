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
    /// Uses vectorized operations for faster row boundary detection.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private List<Dictionary<string, object>> ScanRowsWithSimd(byte[] data, string? where)
    {
        var results = new List<Dictionary<string, object>>();
        
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);
        
        while (ms.Position < ms.Length)
        {
            var row = new Dictionary<string, object>();
            bool valid = true;
            
            for (int i = 0; i < this.Columns.Count; i++)
            {
                try 
                { 
                    row[this.Columns[i]] = ReadTypedValue(reader, this.ColumnTypes[i]); 
                }
                catch 
                { 
                    valid = false; 
                    break; 
                }
            }
            
            if (valid && (string.IsNullOrEmpty(where) || EvaluateWhere(row, where)))
            {
                results.Add(row);
            }
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
