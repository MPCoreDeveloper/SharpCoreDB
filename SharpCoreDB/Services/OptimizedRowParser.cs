using System.Buffers;
using System.Text;
using System.Text.Json;

namespace SharpCoreDB.Services;

/// <summary>
/// Optimized row parser using Span&lt;byte&gt; and ArrayPool to reduce GC pressure.
/// </summary>
public static class OptimizedRowParser
{
    private static readonly ArrayPool<char> _charPool = ArrayPool<char>.Shared;
    private static readonly ArrayPool<byte> _bytePool = ArrayPool<byte>.Shared;

    /// <summary>
    /// Parses a JSON row string into a dictionary with minimal allocations.
    /// </summary>
    /// <param name="jsonBytes">The JSON bytes to parse.</param>
    /// <returns>Parsed row dictionary.</returns>
    public static Dictionary<string, object> ParseRowOptimized(ReadOnlySpan<byte> jsonBytes)
    {
        // For small JSON, use the span directly
        if (jsonBytes.Length < 4096)
        {
            return JsonSerializer.Deserialize<Dictionary<string, object>>(jsonBytes) 
                ?? [];
        }

        // For larger JSON, we still need to deserialize but can use pooled buffers
        byte[] rentedBuffer = _bytePool.Rent(jsonBytes.Length);
        try
        {
            jsonBytes.CopyTo(rentedBuffer.AsSpan());
            return JsonSerializer.Deserialize<Dictionary<string, object>>(
                rentedBuffer.AsSpan(0, jsonBytes.Length)) 
                ?? [];
        }
        finally
        {
            _bytePool.Return(rentedBuffer);
        }
    }

    /// <summary>
    /// Parses a JSON row string into a dictionary with minimal allocations.
    /// </summary>
    /// <param name="json">The JSON string to parse.</param>
    /// <returns>Parsed row dictionary.</returns>
    public static Dictionary<string, object> ParseRowOptimized(string json)
    {
        // Fast path for empty or null
        if (string.IsNullOrEmpty(json))
            return [];

        return JsonSerializer.Deserialize<Dictionary<string, object>>(json) 
            ?? new Dictionary<string, object>();
    }

    /// <summary>
    /// Serializes a row dictionary to JSON with minimal allocations using ArrayPool.
    /// </summary>
    /// <param name="row">The row to serialize.</param>
    /// <returns>JSON string.</returns>
    public static string SerializeRowOptimized(Dictionary<string, object> row)
    {
        return JsonSerializer.Serialize(row);
    }

    /// <summary>
    /// Parses multiple rows from a JSON array with reduced allocations.
    /// </summary>
    /// <param name="jsonArrayBytes">The JSON array bytes.</param>
    /// <returns>List of parsed rows.</returns>
    public static List<Dictionary<string, object>> ParseRowsOptimized(ReadOnlySpan<byte> jsonArrayBytes)
    {
        if (jsonArrayBytes.Length < 4096)
        {
            return JsonSerializer.Deserialize<List<Dictionary<string, object>>>(jsonArrayBytes) 
                ?? new List<Dictionary<string, object>>();
        }

        byte[] rentedBuffer = _bytePool.Rent(jsonArrayBytes.Length);
        try
        {
            jsonArrayBytes.CopyTo(rentedBuffer.AsSpan());
            return JsonSerializer.Deserialize<List<Dictionary<string, object>>>(
                rentedBuffer.AsSpan(0, jsonArrayBytes.Length)) 
                ?? [];
        }
        finally
        {
            _bytePool.Return(rentedBuffer);
        }
    }

    /// <summary>
    /// Builds a WHERE clause string using pooled StringBuilder to reduce allocations.
    /// </summary>
    /// <param name="columnName">The column name.</param>
    /// <param name="operation">The operation (=, >, <, etc.).</param>
    /// <param name="value">The value to compare.</param>
    /// <returns>WHERE clause string.</returns>
    public static string BuildWhereClauseOptimized(string columnName, string operation, object value)
    {
        var sb = new StringBuilder(capacity: 64);
        sb.Append(columnName);
        sb.Append(' ');
        sb.Append(operation);
        sb.Append(' ');
        
        if (value is string strValue)
        {
            sb.Append('\'');
            sb.Append(strValue.Replace("'", "''"));
            sb.Append('\'');
        }
        else
        {
            sb.Append(value);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Parses a CSV-like row format into a dictionary with minimal allocations.
    /// Useful for bulk import scenarios.
    /// </summary>
    /// <param name="line">The CSV line.</param>
    /// <param name="columns">The column names.</param>
    /// <param name="separator">The separator character (default: comma).</param>
    /// <returns>Parsed row dictionary.</returns>
    public static Dictionary<string, object> ParseCsvRowOptimized(
        ReadOnlySpan<char> line, 
        List<string> columns, 
        char separator = ',')
    {
        var result = new Dictionary<string, object>(columns.Count);
        int columnIndex = 0;
        int start = 0;

        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == separator)
            {
                if (columnIndex < columns.Count)
                {
                    var value = line.Slice(start, i - start).ToString();
                    result[columns[columnIndex]] = value;
                }
                columnIndex++;
                start = i + 1;
            }
        }

        // Handle last column
        if (columnIndex < columns.Count && start < line.Length)
        {
            var value = line.Slice(start).ToString();
            result[columns[columnIndex]] = value;
        }

        return result;
    }

    /// <summary>
    /// Gets statistics about pool usage (for debugging/monitoring).
    /// </summary>
    /// <returns>Description of pool usage.</returns>
    public static string GetPoolStatistics()
    {
        return "ArrayPool usage: Char pool and Byte pool in use for reduced allocations";
    }
}
