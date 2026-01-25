// <copyright file="Table.PageBasedScan.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.DataStructures;

using System;
using System.Collections.Generic;
using SharpCoreDB.Storage.Hybrid;

/// <summary>
/// PageBased full table scan implementation for Table.
/// This partial class handles SELECT operations on PageBased storage.
/// </summary>
public partial class Table
{
    /// <summary>
    /// Performs a full table scan on PageBased storage.
    /// Iterates all pages and records belonging to this table.
    /// ✅ FIXED: Uses storage engine's GetAllRecords to avoid file lock conflicts
    /// </summary>
    /// <param name="where">Optional WHERE clause for filtering.</param>
    /// <returns>List of all matching rows.</returns>
    private List<Dictionary<string, object>> ScanPageBasedTable(string? where)
    {
        var results = new List<Dictionary<string, object>>();
        
#if DEBUG
        Console.WriteLine($"[ScanPageBasedTable] Starting scan for table: {Name}");
        Console.WriteLine($"[ScanPageBasedTable] StorageMode: {StorageMode}");
#endif
        
        if (StorageMode != StorageMode.PageBased)
        {
#if DEBUG
            Console.WriteLine($"[ScanPageBasedTable] Wrong storage mode, returning empty");
#endif
            return results;
        }
        
        try
        {
            // ✅ FIX: Use storage engine's GetAllRecords instead of creating new PageManager
            // This avoids file lock conflicts since engine already has PageManager instance
            var engine = GetOrCreateStorageEngine();
#if DEBUG
            Console.WriteLine($"[ScanPageBasedTable] Got storage engine: {engine.GetType().Name}");
#endif
            
            // Iterate all records using the engine
            int recordCount = 0;
            int deserializeFailureCount = 0;
            foreach (var (_, data) in engine.GetAllRecords(Name))
            {
                recordCount++;
#if DEBUG
                if (recordCount <= 3)
                {
                    Console.WriteLine($"[ScanPageBasedTable] Found record #{recordCount}, data length: {data.Length}");
                }
#endif
                
                try
                {
                    // Deserialize to row
                    var row = DeserializeRowFromSpan(data);
                    if (row == null)
                    {
#if DEBUG
                        deserializeFailureCount++;
                        Console.WriteLine($"[ScanPageBasedTable] ❌ Record #{recordCount} failed to deserialize");
#endif
                        continue;
                    }
                    
                    // Apply WHERE filter if specified
                    if (string.IsNullOrEmpty(where) || EvaluateSimpleWhere(row, where))
                    {
                        results.Add(row);
                    }
                }
                catch
                {
                    // Exception during record processing - skip this record and continue
                    // This handles malformed or corrupt records gracefully
#if DEBUG
                    Console.WriteLine($"[ScanPageBasedTable] Exception deserializing record #{recordCount}");
#endif
                }
            }
            
#if DEBUG
            Console.WriteLine($"[ScanPageBasedTable] ✅ Total records found: {recordCount}, deserialization failures: {deserializeFailureCount}, after filtering: {results.Count}");
#endif
        }
        catch
        {
            // Exception during scan initialization - return empty results
            // The WHERE clause evaluation or engine access may have failed
#if DEBUG
            Console.WriteLine($"[ScanPageBasedTable] Exception during scan");
#endif
        }
        
        return results;
    }
    
    /// <summary>
    /// Deserializes a byte array into a row dictionary.
    /// Helper method for PageBased storage scanning.
    /// </summary>
    private Dictionary<string, object>? DeserializeRowFromSpan(byte[] data)
    {
        if (data == null || data.Length == 0)
        {
#if DEBUG
            Console.WriteLine($"[DeserializeRowFromSpan] Data is null or empty");
#endif
            return null;
        }
        
#if DEBUG
        Console.WriteLine($"[DeserializeRowFromSpan] Data length: {data.Length}, Columns: {Columns.Count}");
        if (data.Length >= 16)
        {
            Console.WriteLine($"[DeserializeRowFromSpan] First 16 bytes: {BitConverter.ToString(data, 0, 16)}");
        }
#endif
        
        var row = new Dictionary<string, object>();
        int offset = 0;
        ReadOnlySpan<byte> dataSpan = data.AsSpan();

        try
        {
            for (int i = 0; i < Columns.Count; i++)
            {
                if (offset >= dataSpan.Length)
                {
#if DEBUG
                    Console.WriteLine($"[DeserializeRowFromSpan] Offset {offset} exceeds data length {dataSpan.Length} at column {i}/{Columns.Count}");
#endif
                    return null;
                }

                var value = ReadTypedValueFromSpan(dataSpan.Slice(offset), ColumnTypes[i], out int bytesRead);
#if DEBUG
                Console.WriteLine($"[DeserializeRowFromSpan] Column {i} ({Columns[i]}, {ColumnTypes[i]}): offset={offset}, bytesRead={bytesRead}, value={value}");
#endif
                
                row[Columns[i]] = value;
                offset += bytesRead;
            }

#if DEBUG
            Console.WriteLine($"[DeserializeRowFromSpan] Successfully deserialized row with {row.Count} columns");
#endif
            return row;
        }
#if DEBUG
        catch (Exception ex)
        {
            // Exception during deserialization indicates corrupt data - ignore and return null
            // This row will be skipped during scanning
            Console.WriteLine($"[DeserializeRowFromSpan] Exception during deserialization: {ex.Message}");
            Console.WriteLine($"[DeserializeRowFromSpan] At offset: {offset}, remaining bytes: {dataSpan.Length - offset}");
            if (offset < dataSpan.Length)
            {
                var remaining = Math.Min(16, dataSpan.Length - offset);
                Console.WriteLine($"[DeserializeRowFromSpan] Next {remaining} bytes: {BitConverter.ToString(data, offset, remaining)}");
            }
            return null;
        }
#else
        catch
        {
            // Exception during deserialization indicates corrupt data - ignore and return null
            // This row will be skipped during scanning
            return null;
        }
#endif
    }
    
    /// <summary>
    /// Evaluates a simple WHERE clause against a row.
    /// Supports equality, greater-than, and less-than comparisons.
    /// </summary>
    /// <param name="row">The row to evaluate.</param>
    /// <param name="where">The WHERE clause.</param>
    /// <returns>True if row matches WHERE clause.</returns>
    private static bool EvaluateSimpleWhere(Dictionary<string, object> row, string where)
    {
        // Handle: column > value
        if (where.Contains('>'))
        {
            var parts = where.Split('>');
            if (parts.Length == 2)
            {
                var columnName = parts[0].Trim();
                var valueStr = parts[1].Trim();
                
                if (row.TryGetValue(columnName, out var rowValue) && 
                    int.TryParse(valueStr, out var intValue) && 
                    rowValue is int rowInt)
                {
                    return rowInt > intValue;
                }
            }
            return false;
        }
        
        // Handle: column < value
        if (where.Contains('<'))
        {
            var parts = where.Split('<');
            if (parts.Length == 2)
            {
                var columnName = parts[0].Trim();
                var valueStr = parts[1].Trim();
                
                if (row.TryGetValue(columnName, out var rowValue) && 
                    int.TryParse(valueStr, out var intValue) && 
                    rowValue is int rowInt)
                {
                    return rowInt < intValue;
                }
            }
            return false;
        }
        
        // Handle: column = value
        if (where.Contains('='))
        {
            var parts = where.Split('=');
            if (parts.Length == 2)
            {
                var columnName = parts[0].Trim();
                var valueStr = parts[1].Trim().Trim('\'', '"');
                
                if (row.TryGetValue(columnName, out var rowValue))
                {
                    return rowValue?.ToString() == valueStr;
                }
            }
            return false;
        }
        
        // Default: include row if we can't parse WHERE
        return true;
    }
}
