// <copyright file="JoinExecutor.Diagnostics.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Execution;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Diagnostic extension for JoinExecutor to debug JOIN issues.
/// ✅ C# 14: Modern patterns and debugging helpers.
/// </summary>
public static class JoinExecutorDiagnostics
{
    /// <summary>
    /// Executes a LEFT JOIN with detailed diagnostic logging.
    /// Helps identify where multiple matches are being lost.
    /// </summary>
    public static List<Dictionary<string, object>> ExecuteLeftJoinWithDiagnostics(
        List<Dictionary<string, object>> leftRows,
        List<Dictionary<string, object>> rightRows,
        string? leftAlias,
        string? rightAlias,
        Func<Dictionary<string, object>, Dictionary<string, object>, bool> onCondition,
        string leftTableName = "left",
        string rightTableName = "right")
    {
        Console.WriteLine($"\n[DIAGNOSTICS] LEFT JOIN: {leftTableName} ({leftRows.Count} rows) LEFT JOIN {rightTableName} ({rightRows.Count} rows)");
        
        var result = new List<Dictionary<string, object>>();
        var matchCount = new Dictionary<int, int>(); // leftRowIdx -> match count

        // Execute nested loop join manually with diagnostics
        for (int leftIdx = 0; leftIdx < leftRows.Count; leftIdx++)
        {
            var leftRow = leftRows[leftIdx];
            int matchesForThisRow = 0;

            for (int rightIdx = 0; rightIdx < rightRows.Count; rightIdx++)
            {
                var rightRow = rightRows[rightIdx];
                bool matches = onCondition(leftRow, rightRow);

                if (matches)
                {
                    matchesForThisRow++;
                    var merged = MergeRowsPublic(leftRow, rightRow, leftAlias, rightAlias);
                    result.Add(merged);
                    
                    Console.WriteLine($"  ✓ Left[{leftIdx}] matches Right[{rightIdx}] -> emitted row {result.Count}");
                }
            }

            matchCount[leftIdx] = matchesForThisRow;

            if (matchesForThisRow == 0)
            {
                var merged = MergeRowsPublic(leftRow, null, leftAlias, rightAlias);
                result.Add(merged);
                Console.WriteLine($"  ✗ Left[{leftIdx}] NO MATCHES -> emitted NULL row {result.Count}");
            }
        }

        Console.WriteLine($"[DIAGNOSTICS] Summary:");
        foreach (var (idx, count) in matchCount)
        {
            var leftRow = leftRows[idx];
            var leftValue = GetRowIdentifier(leftRow);
            Console.WriteLine($"  Left[{idx}] ({leftValue}): {count} matches");
        }
        Console.WriteLine($"[DIAGNOSTICS] Total output rows: {result.Count}");

        return result;
    }

    /// <summary>
    /// Public wrapper for MergeRows to use in diagnostics.
    /// </summary>
    private static Dictionary<string, object> MergeRowsPublic(
        Dictionary<string, object>? leftRow,
        Dictionary<string, object>? rightRow,
        string? leftAlias,
        string? rightAlias)
    {
        Dictionary<string, object> resultRow = [];

        if (leftRow is not null)
        {
            foreach (var (key, value) in leftRow)
            {
                string columnName = string.IsNullOrEmpty(leftAlias)
                    ? key
                    : $"{leftAlias}.{key}";
                resultRow[columnName] = value;
            }
        }

        if (rightRow is not null)
        {
            foreach (var (key, value) in rightRow)
            {
                string columnName = string.IsNullOrEmpty(rightAlias)
                    ? key
                    : $"{rightAlias}.{key}";
                resultRow[columnName] = value;
            }
        }

        return resultRow;
    }

    /// <summary>
    /// Gets a human-readable identifier for a row (ID or first value).
    /// </summary>
    private static string GetRowIdentifier(Dictionary<string, object> row)
    {
        if (row.TryGetValue("id", out var id))
            return id?.ToString() ?? "null";
        
        var firstValue = row.Values.FirstOrDefault();
        return firstValue?.ToString() ?? "unknown";
    }

    /// <summary>
    /// Validates that JoinConditionEvaluator is working correctly.
    /// </summary>
    public static void ValidateJoinCondition(
        List<Dictionary<string, object>> leftRows,
        List<Dictionary<string, object>> rightRows,
        Func<Dictionary<string, object>, Dictionary<string, object>, bool> onCondition,
        string leftTableName = "left",
        string rightTableName = "right")
    {
        Console.WriteLine($"\n[CONDITION VALIDATION] Testing {leftTableName} x {rightTableName}");
        Console.WriteLine($"[CONDITION VALIDATION] Left rows: {leftRows.Count}, Right rows: {rightRows.Count}");

        int totalMatches = 0;
        var matchMatrix = new bool[leftRows.Count, rightRows.Count];

        for (int l = 0; l < leftRows.Count; l++)
        {
            for (int r = 0; r < rightRows.Count; r++)
            {
                bool matches = onCondition(leftRows[l], rightRows[r]);
                matchMatrix[l, r] = matches;
                if (matches) totalMatches++;
            }
        }

        // Print match matrix
        Console.WriteLine($"[CONDITION VALIDATION] Match matrix ({leftRows.Count}x{rightRows.Count}):");
        for (int l = 0; l < leftRows.Count; l++)
        {
            var leftId = GetRowIdentifier(leftRows[l]);
            var matches = Enumerable.Range(0, rightRows.Count)
                .Where(r => matchMatrix[l, r])
                .Select(r => GetRowIdentifier(rightRows[r]))
                .ToList();
            
            Console.WriteLine($"  Left[{l}] ({leftId}): matches Right [{string.Join(", ", matches)}]");
        }

        Console.WriteLine($"[CONDITION VALIDATION] Total matches: {totalMatches}");
    }
}
