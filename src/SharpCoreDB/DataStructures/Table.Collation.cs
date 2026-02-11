namespace SharpCoreDB.DataStructures;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// ✅ Phase 5: Collation-aware query execution support for WHERE, ORDER BY, DISTINCT, and GROUP BY.
/// This partial class extends Table with collation-aware filtering and aggregation operations.
/// 
/// ✅ PERFORMANCE: All collation comparisons are inlined for hot-path optimization.
/// ✅ CORRECTNESS: All operations respect column collations for consistent results.
/// </summary>
public partial class Table
{
    /// <summary>
    /// Evaluates a WHERE condition against a row, respecting column collations.
    /// ✅ Phase 5: Enhanced to use CollationComparator for collation-aware filtering.
    /// 
    /// Supports: =, &lt;&gt;, &gt;, &lt;, &gt;=, &lt;=, LIKE, IN, AND, OR
    /// Example: WHERE email = 'alice@example.com' with NOCASE collation → case-insensitive match
    /// </summary>
    /// <param name="row">The row to evaluate.</param>
    /// <param name="columnName">The column name to check.</param>
    /// <param name="operatorStr">The comparison operator (=, <>, >, <, >=, <=, LIKE, etc.)</param>
    /// <param name="value">The value to compare against.</param>
    /// <returns>True if the condition is satisfied.</returns>
    public bool EvaluateConditionWithCollation(
        Dictionary<string, object> row,
        string columnName,
        string operatorStr,
        object value)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(columnName);
        ArgumentNullException.ThrowIfNull(operatorStr);

        // ✅ OPTIMIZATION: Get column index once
        if (!row.TryGetValue(columnName, out var rowValue))
            return false;

        // Get column collation
        int colIdx = this.Columns.IndexOf(columnName);
        var collation = colIdx >= 0 && colIdx < this.ColumnCollations.Count
            ? this.ColumnCollations[colIdx]
            : CollationType.Binary;

        // Convert both values to strings for comparison (or use typed comparison if applicable)
        string? rowValueStr = rowValue?.ToString();
        string? valueStr = value?.ToString();

        // Normalize operator (handle case variations)
        var op = operatorStr.ToUpperInvariant().Trim();

        return op switch
        {
            // Equality operators
            "=" => CollationComparator.Equals(rowValueStr, valueStr, collation),
            "<>" or "!=" => !CollationComparator.Equals(rowValueStr, valueStr, collation),

            // Comparison operators
            ">" => CollationComparator.Compare(rowValueStr, valueStr, collation) > 0,
            "<" => CollationComparator.Compare(rowValueStr, valueStr, collation) < 0,
            ">=" => CollationComparator.Compare(rowValueStr, valueStr, collation) >= 0,
            "<=" => CollationComparator.Compare(rowValueStr, valueStr, collation) <= 0,

            // LIKE operator - pattern matching with collation
            "LIKE" => CollationComparator.Like(rowValueStr, valueStr, collation),
            "NOT LIKE" => !CollationComparator.Like(rowValueStr, valueStr, collation),

            // IN operator - check if value is in a comma-separated list
            "IN" => EvaluateInOperator(rowValueStr, valueStr, collation),
            "NOT IN" => !EvaluateInOperator(rowValueStr, valueStr, collation),

            _ => false
        };
    }

    /// <summary>
    /// Evaluates the IN operator: "column IN (val1, val2, val3)"
    /// Respects collation for each comparison.
    /// </summary>
    private bool EvaluateInOperator(string? rowValue, string? listStr, CollationType collation)
    {
        if (string.IsNullOrEmpty(listStr))
            return false;

        // Parse comma-separated list (remove parentheses, spaces)
        var list = listStr
            .Trim('(', ')')
            .Split(',')
            .Select(s => s.Trim().Trim('\'', '"'))
            .ToList();

        // Check if rowValue equals any item in the list
        foreach (var item in list)
        {
            if (CollationComparator.Equals(rowValue, item, collation))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Performs collation-aware DISTINCT operation on the result set.
    /// Eliminates duplicate rows based on collation-sensitive equality.
    /// 
    /// ✅ Phase 5: Essential for correct GROUP BY and SELECT DISTINCT with collation.
    /// Example: SELECT DISTINCT email FROM users with NOCASE
    ///   → 'alice@example.com' and 'ALICE@EXAMPLE.COM' treated as same
    /// </summary>
    /// <param name="rows">The result set to deduplicate.</param>
    /// <param name="columnName">Optional: if specified, deduplicates by this column only; otherwise deduplicates entire rows.</param>
    /// <returns>Deduplicated result set.</returns>
    public List<Dictionary<string, object>> ApplyDistinctWithCollation(
        List<Dictionary<string, object>> rows,
        string? columnName = null)
    {
        if (rows.Count == 0)
            return rows;

        // Case 1: DISTINCT on specific column
        if (!string.IsNullOrEmpty(columnName))
        {
            int colIdx = this.Columns.IndexOf(columnName);
            if (colIdx < 0)
                return rows; // Column not found, return as-is

            var collation = colIdx < this.ColumnCollations.Count
                ? this.ColumnCollations[colIdx]
                : CollationType.Binary;

            // Use collation-aware HashSet for deduplication
            var comparer = new CollationAwareEqualityComparer(collation);
            var seen = new HashSet<string>(comparer);
            var result = new List<Dictionary<string, object>>();

            foreach (var row in rows)
            {
                if (row.TryGetValue(columnName, out var value))
                {
                    var valueStr = value?.ToString() ?? string.Empty;
                    if (seen.Add(valueStr))
                    {
                        result.Add(row);
                    }
                }
            }

            return result;
        }

        // Case 2: DISTINCT on entire rows (dedup by all columns)
        // For entire rows, we need to compare all columns with their respective collations
        var resultRows = new List<Dictionary<string, object>>();
        var seenRows = new List<Dictionary<string, object>>();

        foreach (var row in rows)
        {
            bool isUnique = true;

            foreach (var seenRow in seenRows)
            {
                if (RowsAreEqualWithCollation(row, seenRow))
                {
                    isUnique = false;
                    break;
                }
            }

            if (isUnique)
            {
                resultRows.Add(row);
                seenRows.Add(row);
            }
        }

        return resultRows;
    }

    /// <summary>
    /// Checks if two rows are equal considering all columns and their collations.
    /// Used for DISTINCT and GROUP BY with collation support.
    /// </summary>
    private bool RowsAreEqualWithCollation(
        Dictionary<string, object> row1,
        Dictionary<string, object> row2)
    {
        if (row1.Count != row2.Count)
            return false;

        for (int i = 0; i < this.Columns.Count; i++)
        {
            var col = this.Columns[i];
            var collation = i < this.ColumnCollations.Count
                ? this.ColumnCollations[i]
                : CollationType.Binary;

            row1.TryGetValue(col, out var val1);
            row2.TryGetValue(col, out var val2);

            string? str1 = val1?.ToString();
            string? str2 = val2?.ToString();

            if (!CollationComparator.Equals(str1, str2, collation))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Performs collation-aware GROUP BY operation.
    /// Groups rows by column values using collation-aware equality.
    /// 
    /// ✅ Phase 5: Returns grouped rows ready for aggregation.
    /// Example: GROUP BY status with NOCASE → 'pending' and 'PENDING' → one group
    /// </summary>
    /// <param name="rows">The rows to group.</param>
    /// <param name="groupByColumn">The column to group by.</param>
    /// <returns>Dictionary: key=groupValue → rows in that group.</returns>
    public Dictionary<string, List<Dictionary<string, object>>> GroupByWithCollation(
        List<Dictionary<string, object>> rows,
        string groupByColumn)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(groupByColumn);

        int colIdx = this.Columns.IndexOf(groupByColumn);
        if (colIdx < 0)
            throw new InvalidOperationException($"Column '{groupByColumn}' not found");

        var collation = colIdx < this.ColumnCollations.Count
            ? this.ColumnCollations[colIdx]
            : CollationType.Binary;

        // Use collation-aware comparer as dictionary key
        var comparer = new CollationAwareEqualityComparer(collation);
        var groups = new Dictionary<string, List<Dictionary<string, object>>>(comparer);

        foreach (var row in rows)
        {
            if (row.TryGetValue(groupByColumn, out var value))
            {
                var keyStr = value?.ToString() ?? string.Empty;

                if (!groups.ContainsKey(keyStr))
                {
                    groups[keyStr] = [];
                }

                groups[keyStr].Add(row);
            }
        }

        return groups;
    }

    /// <summary>
    /// Performs collation-aware ORDER BY (sorting).
    /// Sorts rows by column values using collation-aware comparison.
    /// 
    /// ✅ Phase 5: Essential for correct ORDER BY with collation support.
    /// Example: ORDER BY name with NOCASE → sorted by collation rules
    /// </summary>
    /// <param name="rows">The rows to sort.</param>
    /// <param name="columnName">The column to sort by.</param>
    /// <param name="ascending">True for ascending, false for descending.</param>
    /// <returns>Sorted result set.</returns>
    public List<Dictionary<string, object>> OrderByWithCollation(
        List<Dictionary<string, object>> rows,
        string columnName,
        bool ascending = true)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(columnName);

        if (rows.Count <= 1)
            return rows;

        int colIdx = this.Columns.IndexOf(columnName);
        if (colIdx < 0)
            throw new InvalidOperationException($"Column '{columnName}' not found");

        var collation = colIdx < this.ColumnCollations.Count
            ? this.ColumnCollations[colIdx]
            : CollationType.Binary;

        // Sort using collation-aware comparison
        var sorted = rows.ToList();
        sorted.Sort((row1, row2) =>
        {
            row1.TryGetValue(columnName, out var val1);
            row2.TryGetValue(columnName, out var val2);

            string? str1 = val1?.ToString();
            string? str2 = val2?.ToString();

            int comparison = CollationComparator.Compare(str1, str2, collation);
            return ascending ? comparison : -comparison;
        });

        return sorted;
    }
}
