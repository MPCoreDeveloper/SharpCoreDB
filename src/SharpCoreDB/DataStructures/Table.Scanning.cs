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
    /// ✅ OPTIMIZED: Early WHERE predicate push-down skips full deserialization for non-matching rows.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private List<Dictionary<string, object>> ScanRowsWithSimd(byte[] data, string? where)
    {
        var results = new List<Dictionary<string, object>>();

        // ✅ PERF: Pre-parse a simple "col = 'val'" WHERE once so non-matching records
        // are skipped without deserializing unused columns. Only for STRING columns with
        // Binary collation — other collations require collation-aware EvaluateWhere.
        int earlyWhereColIdx = -1;
        string? earlyWhereValue = null;
        if (!string.IsNullOrEmpty(where) &&
            TryParseSimpleWhereClause(where, out var ewCol, out var ewValObj) &&
            ewValObj is string ewStr)
        {
            int idx = this.Columns.IndexOf(ewCol);
            if (idx >= 0 && idx < this.ColumnTypes.Count && this.ColumnTypes[idx] == DataType.String)
            {
                var collation = idx < this.ColumnCollations.Count
                    ? this.ColumnCollations[idx]
                    : CollationType.Binary;

                if (collation == CollationType.Binary)
                {
                    earlyWhereColIdx = idx;
                    earlyWhereValue = ewStr;
                }
            }
        }

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
            
            // ✅ C# 14: Range operator - extract length prefix span first
            var lengthSpan = dataSpan[filePosition..(filePosition + 4)];
            int recordLength = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(lengthSpan);
            
            // Sanity check: record length must be reasonable
            const int MaxRecordSize = 1_000_000_000; // 1 GB max per record
            if (recordLength < 0 || recordLength > MaxRecordSize)
            {
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
                break;
            }
            
            // ✅ C# 14: Range operator for record data extraction
            int dataOffset = filePosition + 4;
            ReadOnlySpan<byte> recordData = dataSpan[dataOffset..(dataOffset + recordLength)];

            // ✅ PERF: Early WHERE check — read only columns 0..whereColIdx, then check predicate
            // before allocating a Dictionary. Saves ~4 column reads for non-matching rows.
            if (earlyWhereColIdx >= 0 && earlyWhereValue != null)
            {
                bool earlyMismatch = false;
                int checkOffset = 0;
                for (int ci = 0; ci <= earlyWhereColIdx && checkOffset < recordData.Length; ci++)
                {
                    try
                    {
                        var val = ReadTypedValueFromSpan(recordData[checkOffset..], this.ColumnTypes[ci], out int br);
                        checkOffset += br;
                        if (ci == earlyWhereColIdx)
                        {
                            var vs = val == DBNull.Value ? null : val?.ToString();
                            if (!string.Equals(vs, earlyWhereValue, StringComparison.Ordinal))
                                earlyMismatch = true;
                        }
                    }
                    catch
                    {
                        earlyMismatch = true;
                        break;
                    }
                }

                if (earlyMismatch)
                {
                    filePosition += 4 + recordLength;
                    continue;
                }
            }

            // Parse the record into a row (only for WHERE-matching or no-simple-WHERE records)
            var row = new Dictionary<string, object>();
            bool valid = true;
            int offset = 0;
            
            for (int i = 0; i < this.Columns.Count; i++)
            {
                try
                {
                    // ✅ C# 14: Range operator for typed value parsing
                    var value = ReadTypedValueFromSpan(recordData[offset..], this.ColumnTypes[i], out int bytesRead);
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

    /// <summary>
    /// Evaluates a WHERE clause against a row.
    /// Supports operators: equals, not equals, greater than, less than, greater or equal, less or equal,
    /// IS NULL, IS NOT NULL, AND, OR.
    /// </summary>
    private bool EvaluateWhere(Dictionary<string, object> row, string? where)
    {
        if (string.IsNullOrEmpty(where)) return true;

        var parts = where.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return true;

        // ✅ FIX: Detect complex WHERE clauses (contains AND/OR) and delegate to SqlParser.EvaluateJoinWhere
        // which has full support for compound conditions
        var whereUpper = where.ToUpperInvariant();
        if (whereUpper.Contains(" AND ") || whereUpper.Contains(" OR "))
        {
            return SqlParser.EvaluateJoinWhere(row, where);
        }

        var columnName = parts[0].Trim('"', '[', ']', '`');

        // ✅ FIX: Strip table alias prefix from column names (e.g., "u"."Username" → Username)
        // EF Core generates SQL with aliased column references like "u"."ColumnName".
        // After splitting on spaces and trimming quotes, the column reference becomes u"."ColumnName
        // We need to extract just the column name after the last dot-quote separator.
        var dotIdx = columnName.LastIndexOf('.');
        if (dotIdx >= 0 && dotIdx < columnName.Length - 1)
        {
            columnName = columnName[(dotIdx + 1)..].Trim('"', '[', ']', '`');
        }

        // Handle IS NOT NULL (4 tokens: column IS NOT NULL)
        if (parts.Length >= 4
            && parts[1].Equals("IS", StringComparison.OrdinalIgnoreCase)
            && parts[2].Equals("NOT", StringComparison.OrdinalIgnoreCase)
            && parts[3].Equals("NULL", StringComparison.OrdinalIgnoreCase))
        {
            return row.TryGetValue(columnName, out var v) && v is not null && v is not DBNull;
        }

        // Handle IS NULL (3 tokens: column IS NULL)
        if (parts[1].Equals("IS", StringComparison.OrdinalIgnoreCase)
            && parts[2].Equals("NULL", StringComparison.OrdinalIgnoreCase))
        {
            return !row.TryGetValue(columnName, out var v) || v is null || v is DBNull;
        }

        var op = parts[1];
        var value = parts[2].Trim('"').Trim((char)39);

        // Handle multi-word operators: NOT REGEXP, NOT LIKE, NOT IN (4 tokens: col NOT OP val)
        if (parts.Length >= 4
            && parts[1].Equals("NOT", StringComparison.OrdinalIgnoreCase)
            && (parts[2].Equals("REGEXP", StringComparison.OrdinalIgnoreCase)
                || parts[2].Equals("LIKE", StringComparison.OrdinalIgnoreCase)
                || parts[2].Equals("IN", StringComparison.OrdinalIgnoreCase)))
        {
            op = $"NOT {parts[2].ToUpperInvariant()}";
            value = parts[3].Trim('"').Trim((char)39);
        }

        if (!row.TryGetValue(columnName, out var rowValue) || rowValue == null)
            return false;

        if (rowValue is string)
        {
            var colIdx = this.Columns.IndexOf(columnName);
            var collation = colIdx >= 0 && colIdx < this.ColumnCollations.Count
                ? this.ColumnCollations[colIdx]
                : CollationType.Binary;
            var localeName = colIdx >= 0 && colIdx < this.ColumnLocaleNames.Count
                ? this.ColumnLocaleNames[colIdx]
                : null;

            if (collation == CollationType.Locale && !string.IsNullOrWhiteSpace(localeName))
            {
                return EvaluateConditionWithLocale(row, columnName, op, value, localeName);
            }

            return EvaluateConditionWithCollation(row, columnName, op, value);
        }

        // Handle different operators for non-string values
        switch (op)
        {
            case "=":
                return rowValue.ToString() == value;

            case "!=":
            case "<>":
                return rowValue.ToString() != value;

            case ">":
                return CompareValues(rowValue, value) > 0;

            case "<":
                return CompareValues(rowValue, value) < 0;

            case ">=":
                return CompareValues(rowValue, value) >= 0;

            case "<=":
                return CompareValues(rowValue, value) <= 0;

            default:
                return true; // Unsupported operator - don't filter
        }
    }
    
    /// <summary>
    /// Compares two values for ordering (supports numbers and strings).
    /// </summary>
    private static int CompareValues(object rowValue, string compareValue)
    {
        // Try numeric comparison first
        if (rowValue is int intVal && int.TryParse(compareValue, out var intCompare))
            return intVal.CompareTo(intCompare);
            
        if (rowValue is long longVal && long.TryParse(compareValue, out var longCompare))
            return longVal.CompareTo(longCompare);
            
        if (rowValue is double doubleVal && double.TryParse(compareValue, out var doubleCompare))
            return doubleVal.CompareTo(doubleCompare);
            
        if (rowValue is decimal decimalVal && decimal.TryParse(compareValue, out var decimalCompare))
            return decimalVal.CompareTo(decimalCompare);
        
        // Fallback to string comparison
        return string.Compare(rowValue.ToString(), compareValue, StringComparison.Ordinal);
    }
}
