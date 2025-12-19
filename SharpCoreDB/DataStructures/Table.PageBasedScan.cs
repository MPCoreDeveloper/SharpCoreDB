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
    /// <param name="tableId">The table ID to scan (unused now, kept for signature compatibility).</param>
    /// <param name="where">Optional WHERE clause for filtering.</param>
    /// <returns>List of all matching rows.</returns>
    private List<Dictionary<string, object>> ScanPageBasedTable(uint tableId, string? where)
    {
        var results = new List<Dictionary<string, object>>();
        
        if (StorageMode != StorageMode.PageBased)
        {
            return results;
        }
        
        try
        {
            // ✅ FIX: Use storage engine's GetAllRecords instead of creating new PageManager
            // This avoids file lock conflicts since engine already has PageManager instance
            var engine = GetOrCreateStorageEngine();
            
            // Iterate all records using the engine
            foreach (var (storageRef, data) in engine.GetAllRecords(Name))
            {
                try
                {
                    // Deserialize to row
                    var row = DeserializeRowFromSpan(data);
                    if (row == null)
                    {
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
                    // Skip corrupted records
                }
            }
        }
        catch
        {
            // Return partial results on error
        }
        
        return results;
    }
    
    /// <summary>
    /// Deserializes a byte array into a row dictionary.
    /// Helper method for PageBased storage scanning.
    /// </summary>
    private Dictionary<string, object>? DeserializeRowFromSpan(byte[] data)
    {
        if (data == null || data.Length == 0) return null;
        
        var row = new Dictionary<string, object>();
        int offset = 0;
        ReadOnlySpan<byte> dataSpan = data.AsSpan();

        try
        {
            for (int i = 0; i < Columns.Count; i++)
            {
                if (offset >= dataSpan.Length)
                    return null;

                var value = ReadTypedValueFromSpan(dataSpan.Slice(offset), ColumnTypes[i], out int bytesRead);
                row[Columns[i]] = value;
                offset += bytesRead;
            }

            return row;
        }
        catch
        {
            return null;
        }
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
