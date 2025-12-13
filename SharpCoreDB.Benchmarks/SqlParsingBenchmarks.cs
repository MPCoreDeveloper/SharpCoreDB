// <copyright file="SqlParsingBenchmarks.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using System.Text;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Benchmarks for SQL parsing operations.
/// Compares traditional string.Split() vs. zero-allocation Span-based parsing.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class SqlParsingBenchmarks
{
    private string[] sqlQueries = null!;
    private string simpleSelect = null!;
    private string complexSelect = null!;
    private string createTable = null!;
    private string insertQuery = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Simple SELECT
        simpleSelect = "SELECT * FROM users WHERE id = 1";

        // Complex SELECT with JOIN
        complexSelect = "SELECT u.id, u.name, p.title FROM users u LEFT JOIN posts p ON u.id = p.user_id WHERE u.active = 1 ORDER BY u.name ASC LIMIT 10 OFFSET 5";

        // CREATE TABLE
        createTable = "CREATE TABLE users (id INTEGER PRIMARY KEY AUTO, name TEXT, email TEXT, created DATETIME, active BOOLEAN)";

        // INSERT
        insertQuery = "INSERT INTO users (name, email, active) VALUES ('John Doe', 'john@example.com', true)";

        // Array of various queries
        sqlQueries = new[]
        {
            simpleSelect,
            complexSelect,
            createTable,
            insertQuery,
            "UPDATE users SET name = 'Jane' WHERE id = 5",
            "DELETE FROM users WHERE id = 10",
            "SELECT COUNT(*) FROM posts",
            "SELECT * FROM users ORDER BY created DESC LIMIT 100"
        };
    }

    // ==================== TOKENIZATION ====================

    [Benchmark(Baseline = true, Description = "Tokenize: string.Split (allocates)")]
    public string[] Tokenize_StringSplit()
    {
        // Traditional approach - allocates array + strings
        return simpleSelect.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    [Benchmark(Description = "Tokenize: Span-based (zero-alloc)")]
    public int Tokenize_SpanBased()
    {
        // Zero-allocation tokenization using Span
        ReadOnlySpan<char> sql = simpleSelect.AsSpan();
        int tokenCount = 0;
        int position = 0;

        while (position < sql.Length)
        {
            // Skip whitespace
            while (position < sql.Length && char.IsWhiteSpace(sql[position]))
                position++;

            if (position >= sql.Length)
                break;

            // Find token end
            int start = position;
            while (position < sql.Length && !char.IsWhiteSpace(sql[position]))
                position++;

            // Token found (no allocation - just slicing)
            var token = sql.Slice(start, position - start);
            tokenCount++;
        }

        return tokenCount;
    }

    // ==================== KEYWORD MATCHING ====================

    [Benchmark(Description = "Keyword Match: ToUpper + Equals (allocates)")]
    public int KeywordMatch_Traditional()
    {
        int matches = 0;
        var parts = simpleSelect.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var part in parts)
        {
            // ToUpper allocates a new string
            var upper = part.ToUpper();
            if (upper == "SELECT" || upper == "FROM" || upper == "WHERE")
                matches++;
        }
        
        return matches;
    }

    [Benchmark(Description = "Keyword Match: Span.Equals (zero-alloc)")]
    public int KeywordMatch_Optimized()
    {
        int matches = 0;
        ReadOnlySpan<char> sql = simpleSelect.AsSpan();
        int position = 0;

        while (position < sql.Length)
        {
            // Skip whitespace
            while (position < sql.Length && char.IsWhiteSpace(sql[position]))
                position++;

            if (position >= sql.Length)
                break;

            // Find token end
            int start = position;
            while (position < sql.Length && !char.IsWhiteSpace(sql[position]))
                position++;

            // Token span (no allocation)
            var token = sql.Slice(start, position - start);
            
            // Case-insensitive comparison (no allocation)
            if (token.Equals("SELECT", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("FROM", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("WHERE", StringComparison.OrdinalIgnoreCase))
                matches++;
        }

        return matches;
    }

    // ==================== SUBSTRING EXTRACTION ====================

    [Benchmark(Description = "Extract Table: Substring (allocates)")]
    public string ExtractTable_Substring()
    {
        // Traditional: multiple allocations
        var parts = simpleSelect.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var fromIndex = Array.IndexOf(parts, "FROM");
        return fromIndex >= 0 && fromIndex + 1 < parts.Length ? parts[fromIndex + 1] : string.Empty;
    }

    [Benchmark(Description = "Extract Table: Span slicing (zero-alloc)")]
    public ReadOnlySpan<char> ExtractTable_Span()
    {
        // Zero-allocation span slicing
        ReadOnlySpan<char> sql = simpleSelect.AsSpan();
        
        // Find "FROM" keyword
        int fromIndex = sql.IndexOf("FROM", StringComparison.OrdinalIgnoreCase);
        if (fromIndex < 0)
            return ReadOnlySpan<char>.Empty;
        
        // Skip "FROM" and whitespace
        int tableStart = fromIndex + 4;
        while (tableStart < sql.Length && char.IsWhiteSpace(sql[tableStart]))
            tableStart++;
        
        // Find table name end
        int tableEnd = tableStart;
        while (tableEnd < sql.Length && !char.IsWhiteSpace(sql[tableEnd]))
            tableEnd++;
        
        return sql.Slice(tableStart, tableEnd - tableStart);
    }

    // ==================== PARAMETER BINDING ====================

    [Benchmark(Description = "Bind Parameters: string.Replace (allocates)")]
    public string BindParameters_Traditional()
    {
        var sql = "SELECT * FROM users WHERE id = ? AND name = ?";
        var param1 = "123";
        var param2 = "John";
        
        // Each Replace allocates a new string
        sql = sql.Replace("?", param1);
        sql = sql.Replace("?", param2);
        
        return sql;
    }

    [Benchmark(Description = "Bind Parameters: StringBuilder (fewer allocs)")]
    public string BindParameters_StringBuilder()
    {
        var sql = "SELECT * FROM users WHERE id = ? AND name = ?";
        var param1 = "123";
        var param2 = "John";
        
        var sb = new StringBuilder(sql.Length + param1.Length + param2.Length);
        int position = 0;
        int paramIndex = 0;
        var parameters = new[] { param1, param2 };
        
        for (int i = 0; i < sql.Length; i++)
        {
            if (sql[i] == '?' && paramIndex < parameters.Length)
            {
                sb.Append(parameters[paramIndex++]);
            }
            else
            {
                sb.Append(sql[i]);
            }
        }
        
        return sb.ToString();
    }

    // ==================== COMPLEX QUERY PARSING ====================

    [Benchmark(Description = "Parse Complex: Traditional (many allocs)")]
    public int ParseComplex_Traditional()
    {
        // Parse complex SELECT with JOIN
        var parts = complexSelect.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        int selectIdx = Array.IndexOf(parts, "SELECT");
        int fromIdx = Array.IndexOf(parts, "FROM");
        int joinIdx = Array.IndexOf(parts, "JOIN");
        int whereIdx = Array.IndexOf(parts, "WHERE");
        int orderIdx = Array.IndexOf(parts, "ORDER");
        int limitIdx = Array.IndexOf(parts, "LIMIT");
        
        return parts.Length;
    }

    [Benchmark(Description = "Parse Complex: Span-based (zero-alloc)")]
    public int ParseComplex_Optimized()
    {
        // Parse using Span operations
        ReadOnlySpan<char> sql = complexSelect.AsSpan();
        
        int selectIdx = sql.IndexOf("SELECT", StringComparison.OrdinalIgnoreCase);
        int fromIdx = sql.IndexOf("FROM", StringComparison.OrdinalIgnoreCase);
        int joinIdx = sql.IndexOf("JOIN", StringComparison.OrdinalIgnoreCase);
        int whereIdx = sql.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase);
        int orderIdx = sql.IndexOf("ORDER", StringComparison.OrdinalIgnoreCase);
        int limitIdx = sql.IndexOf("LIMIT", StringComparison.OrdinalIgnoreCase);
        
        // Count tokens using Span
        int tokenCount = 0;
        int position = 0;
        
        while (position < sql.Length)
        {
            while (position < sql.Length && char.IsWhiteSpace(sql[position]))
                position++;
            if (position >= sql.Length)
                break;
            
            while (position < sql.Length && !char.IsWhiteSpace(sql[position]))
                position++;
            tokenCount++;
        }
        
        return tokenCount;
    }

    // ==================== PARSE CREATE TABLE ====================

    [Benchmark(Description = "Parse CREATE TABLE: Traditional")]
    public int ParseCreateTable_Traditional()
    {
        // Extract column definitions
        var startIdx = createTable.IndexOf('(');
        var endIdx = createTable.LastIndexOf(')');
        var columnsStr = createTable.Substring(startIdx + 1, endIdx - startIdx - 1);
        
        // Split columns (allocates array)
        var columns = columnsStr.Split(',');
        
        int columnCount = 0;
        foreach (var col in columns)
        {
            // Split column definition (allocates array)
            var parts = col.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            columnCount++;
        }
        
        return columnCount;
    }

    [Benchmark(Description = "Parse CREATE TABLE: Span-based")]
    public int ParseCreateTable_Optimized()
    {
        ReadOnlySpan<char> sql = createTable.AsSpan();
        
        // Find column definitions
        int startIdx = sql.IndexOf('(');
        int endIdx = sql.LastIndexOf(')');
        
        if (startIdx < 0 || endIdx < 0)
            return 0;
        
        var columnsSpan = sql.Slice(startIdx + 1, endIdx - startIdx - 1);
        
        // Count columns by counting commas + 1
        int columnCount = 1;
        for (int i = 0; i < columnsSpan.Length; i++)
        {
            if (columnsSpan[i] == ',')
                columnCount++;
        }
        
        return columnCount;
    }

    // ==================== BATCH PARSING ====================

    [Benchmark(Description = "Parse Batch (8 queries): Traditional")]
    public int ParseBatch_Traditional()
    {
        int totalTokens = 0;
        
        foreach (var query in sqlQueries)
        {
            var parts = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            totalTokens += parts.Length;
        }
        
        return totalTokens;
    }

    [Benchmark(Description = "Parse Batch (8 queries): Span-based")]
    public int ParseBatch_Optimized()
    {
        int totalTokens = 0;
        
        foreach (var query in sqlQueries)
        {
            ReadOnlySpan<char> sql = query.AsSpan();
            int position = 0;
            
            while (position < sql.Length)
            {
                while (position < sql.Length && char.IsWhiteSpace(sql[position]))
                    position++;
                if (position >= sql.Length)
                    break;
                
                while (position < sql.Length && !char.IsWhiteSpace(sql[position]))
                    position++;
                totalTokens++;
            }
        }
        
        return totalTokens;
    }

    // ==================== STRING BUILDER VS SPAN ====================

    [Benchmark(Description = "Build SQL: StringBuilder")]
    public string BuildSql_StringBuilder()
    {
        var sb = new StringBuilder(256);
        sb.Append("SELECT ");
        sb.Append("id, name, email ");
        sb.Append("FROM users ");
        sb.Append("WHERE active = 1 ");
        sb.Append("ORDER BY name ASC");
        return sb.ToString();
    }

    [Benchmark(Description = "Build SQL: String Concat (multiple allocs)")]
    public string BuildSql_Concat()
    {
        return "SELECT " + "id, name, email " + "FROM users " + 
               "WHERE active = 1 " + "ORDER BY name ASC";
    }

    [Benchmark(Description = "Build SQL: String.Format")]
    public string BuildSql_Format()
    {
        return string.Format("SELECT {0} FROM {1} WHERE {2} ORDER BY {3}",
                           "id, name, email", "users", "active = 1", "name ASC");
    }
}
