// <copyright file="SqlParser.Helpers.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

using SharpCoreDB.DataStructures;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// SqlParser partial class containing helper methods:
/// Parameter binding, value parsing, SQL sanitization, WHERE clause parsing, etc.
/// OPTIMIZED: Uses StringBuilder for O(n) parameter binding, HashSet for deduplication.
/// </summary>
public partial class SqlParser
{
    /// <summary>
    /// Detects the "( SELECT" subquery-start pattern without allocating a Regex Match.
    /// Span scan replicating SubqueryStartRegex: "\(\s*SELECT\b" (IgnoreCase, CultureInvariant).
    /// </summary>
    private static bool HasSubqueryStart(ReadOnlySpan<char> sql)
    {
        for (int i = 0; i < sql.Length; i++)
        {
            if (sql[i] != '(')
                continue;

            int j = i + 1;
            while (j < sql.Length && char.IsWhiteSpace(sql[j]))
            {
                j++;
            }

            if (j + 6 > sql.Length || !sql.Slice(j, 6).Equals("SELECT", StringComparison.OrdinalIgnoreCase))
                continue;

            // Word boundary after "SELECT": next char is absent or not a word char.
            int k = j + 6;
            if (k >= sql.Length || (!char.IsLetterOrDigit(sql[k]) && sql[k] != '_'))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Extracts the main query's table name from the FROM clause, ignoring subqueries.
    /// Handles cases like: SELECT c.name, (SELECT MAX(amount) FROM orders) FROM customers
    /// Returns: "customers" (not "orders" which is inside a subquery).
    /// </summary>
    private static string? ExtractMainTableNameFromSql(string sql, int fromKeywordIndex)
    {
        int parenthesisDepth = 0;
        int fromPosition = -1;
        
        // Find the FROM keyword at parenthesis depth 0 (main query level)
        for (int i = fromKeywordIndex; i < sql.Length - 4; i++)
        {
            char c = sql[i];
            
            if (c == '(')
            {
                parenthesisDepth++;
            }
            else if (c == ')')
            {
                parenthesisDepth--;
            }
            else if (parenthesisDepth == 0 && i + 4 <= sql.Length)
            {
                // Check for FROM keyword at depth 0.
                // PERF: span equality — the previous sql.Substring(i, 4).ToUpperInvariant()
                // allocated twice per scanned character position.
                if (sql.AsSpan(i, 4).Equals(SqlConstants.FROM, StringComparison.OrdinalIgnoreCase) &&
                    (i == 0 || char.IsWhiteSpace(sql[i - 1])) && 
                    (i + 4 >= sql.Length || char.IsWhiteSpace(sql[i + 4])))
                {
                    fromPosition = i + 4;
                    break;
                }
            }
        }
        
        if (fromPosition < 0)
        {
            return null;
        }
        
        // Extract table name after FROM keyword
        // Skip whitespace
        while (fromPosition < sql.Length && char.IsWhiteSpace(sql[fromPosition]))
        {
            fromPosition++;
        }
        
        if (fromPosition >= sql.Length)
        {
            return null;
        }
        
        // Extract identifier (table name): stops at whitespace or special characters.
        // PERF: single span scan + one Substring — the previous StringBuilder.Append-per-char
        // loop was allocation-heavy on the legacy SELECT path.
        int end = fromPosition;
        while (end < sql.Length && !char.IsWhiteSpace(sql[end]) && sql[end] != ',' && sql[end] != '(' && sql[end] != ')' && sql[end] != ';')
        {
            end++;
        }
        
        // Strip SQL identifier quotes: "name", [name], `name` (same behavior as the legacy builder).
        ReadOnlySpan<char> name = sql.AsSpan(fromPosition, end - fromPosition).Trim();
        if (name.Length >= 2)
        {
            if ((name[0] == '"' && name[^1] == '"') ||
                (name[0] == '[' && name[^1] == ']') ||
                (name[0] == '`' && name[^1] == '`'))
            {
                name = name[1..^1];
            }
        }

        return name.IsEmpty ? null : name.ToString();
    }

    /// <summary>
    /// Calculates the end index for WHERE clause parsing based on the presence of ORDER BY and LIMIT clauses.
    /// </summary>
    private static int CalculateWhereClauseEndIndex(int orderIdx, int limitIdx, int partsLength)
    {
        if (orderIdx > 0)
        {
            return orderIdx;
        }
        
        if (limitIdx > 0)
        {
            return limitIdx;
        }
        
        return partsLength;
    }

    /// <summary>
    /// Parses a WHERE clause to extract the column names being used.
    /// OPTIMIZED: Uses HashSet for automatic deduplication (25-35% faster).
    /// </summary>
    private static List<string> ParseWhereColumns(string where)
    {
        var columns = new HashSet<string>(); // ✅ Auto-dedup, no Distinct() needed
        var tokens = where.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if ((i % 4 == 0 || (i > 0 && (tokens[i-1] == "AND" || tokens[i-1] == "OR"))) 
                && !string.IsNullOrEmpty(token) && char.IsLetter(token[0]))
            {
                columns.Add(token);
            }
        }
        return [.. columns]; // C# 12 collection expression
    }

    /// <summary>
    /// Parses a string value to the specified data type.
    /// Made public for use in batch INSERT optimization.
    /// </summary>
    public static object? ParseValue(string val, DataType type)
    {
        if (val == "NULL")
        {
            return null;
        }

        while (val.Length >= 2 &&
               ((val.StartsWith('\'') && val.EndsWith('\'')) ||
                (val.StartsWith('"') && val.EndsWith('"'))))
        {
            val = val[1..^1];
        }

        try
        {
            if (type == DataType.Boolean)
            {
                var lower = val.ToLowerInvariant();
                if (lower == "1" || lower == "true") return true;
                if (lower == "0" || lower == "false") return false;
                if (int.TryParse(val, out var intBool))
                {
                    return intBool != 0;
                }
                return bool.Parse(val);
            }

            return type switch
            {
                // ✅ FIX (Known Issue 6): Actionable overflow message instead of the generic
                // "Value was either too large or too small for an Int32". Values like
                // DateTime.UtcNow.Ticks (~6.4e17) exceed Int32; point the user to BIGINT/LONG
                // or the opt-in DatabaseConfig.UseSqliteIntegerAffinity (SQLite INTEGER → Int64).
                DataType.Integer => ParseInt32Checked(val),
                DataType.String => val,
                DataType.Real => double.Parse(val, System.Globalization.CultureInfo.InvariantCulture),
                DataType.Blob => Convert.FromBase64String(val),
                DataType.DateTime => ParseDateTime(val),
                DataType.Long => long.Parse(val, System.Globalization.CultureInfo.InvariantCulture),
                DataType.Decimal => decimal.Parse(val, System.Globalization.CultureInfo.InvariantCulture),
                DataType.Ulid => Ulid.Parse(val),
                DataType.Guid => Guid.Parse(val),
                DataType.RowRef => long.Parse(val, System.Globalization.CultureInfo.InvariantCulture),
                DataType.Vector => ParseVectorValue(val),
                _ => val,
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Invalid value '{val}' for data type {type}: {ex.Message}");
        }
    }

    /// <summary>
    /// Parses an Int32 from string with an actionable overflow message.
    /// ✅ FIX (Known Issue 6): When the value exceeds Int32, tell the user how to resolve it
    /// (use BIGINT/LONG or enable DatabaseConfig.UseSqliteIntegerAffinity for SQLite semantics).
    /// </summary>
    private static int ParseInt32Checked(string val)
    {
        try
        {
            return int.Parse(val, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (OverflowException)
        {
            throw new InvalidOperationException(
                $"Value '{val}' exceeds the Int32 range for an INTEGER column. " +
                $"Declare the column as BIGINT or LONG, or set " +
                $"DatabaseConfig.UseSqliteIntegerAffinity = true to map INTEGER to Int64 (SQLite behavior).");
        }
    }

    /// <summary>
    /// Parses DateTime from string with support for multiple formats.
    /// Handles: 'yyyy-MM-dd HH:mm:ss', 'yyyy-MM-dd', ISO8601, etc.
    /// </summary>
    private static DateTime ParseDateTime(string val)
    {
        // Try common formats first for performance
        string[] formats = [
            "yyyy-MM-dd HH:mm:ss",     // SharpCoreDB default
            "yyyy-MM-dd'T'HH:mm:ss",   // ISO 8601
            "yyyy-MM-dd",               // Date only
            "yyyy-MM-ddTHH:mm:ssZ",    // ISO 8601 with Z
            "yyyy-MM-dd HH:mm:ss.fff"  // With milliseconds
        ];

        if (DateTime.TryParseExact(val, formats, 
            System.Globalization.CultureInfo.InvariantCulture, 
            System.Globalization.DateTimeStyles.None, 
            out var result))
        {
            // ✅ FIX: Always specify UTC kind for consistent storage
            return DateTime.SpecifyKind(result, DateTimeKind.Utc);
        }

        // Fallback to general parse
        var parsed = DateTime.Parse(val, System.Globalization.CultureInfo.InvariantCulture);
        return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
    }

    /// <summary>
    /// Parses a vector value from a JSON float array string or Base64 bytes.
    /// Supports: "[0.1, 0.2, 0.3]" JSON format and Base64-encoded binary.
    /// </summary>
    private static object ParseVectorValue(string val)
    {
        val = val.Trim();

        // JSON array format: [0.1, 0.2, 0.3, ...]
        if (val.StartsWith('[') && val.EndsWith(']'))
        {
            return System.Text.Json.JsonSerializer.Deserialize<float[]>(val)
                ?? throw new InvalidOperationException("Failed to parse vector JSON array");
        }

        // Base64-encoded binary format
        var bytes = Convert.FromBase64String(val);
        return System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(bytes.AsSpan()).ToArray();
    }

    /// <summary>
    /// Evaluates a WHERE clause condition for JOIN operations.
    /// </summary>
    public static bool EvaluateJoinWhere(Dictionary<string, object> row, string where)
    {
        if (string.IsNullOrEmpty(where))
        {
            return true;
        }

        var parts = where.Split(' ');
        if (parts.Length <= 3)
        {
            return EvaluateSimpleCondition(row, parts);
        }
        else
        {
            return EvaluateComplexCondition(row, parts);
        }
    }

    /// <summary>
    /// Evaluates a simple condition (single comparison).
    /// </summary>
    private static bool EvaluateSimpleCondition(Dictionary<string, object> row, string[] parts)
    {
        var key = parts[0].Trim();
        var op = parts[1].Trim();

        // ✅ Parity (BETWEEN): "price BETWEEN 10 AND 16" -> parts = [price, BETWEEN, 10, AND, 16]
        if (op.Equals("BETWEEN", StringComparison.OrdinalIgnoreCase) && parts.Length >= 5)
        {
            var low = parts[2].Trim().Trim('\'', '"');
            var high = parts[4].Trim().Trim('\'', '"');
            return EvaluateBetween(row, key, low, high);
        }

        // Handle IS NOT NULL (parts: key, IS, NOT, NULL)
        if (op.Equals("IS", StringComparison.OrdinalIgnoreCase)
            && parts.Length >= 4
            && parts[2].Trim().Equals("NOT", StringComparison.OrdinalIgnoreCase)
            && parts[3].Trim().Equals("NULL", StringComparison.OrdinalIgnoreCase))
        {
            return row.TryGetValue(key, out var v) && v is not null && v is not DBNull;
        }

        // Handle IS NULL (parts: key, IS, NULL)
        if (op.Equals("IS", StringComparison.OrdinalIgnoreCase)
            && parts.Length >= 3
            && parts[2].Trim().Equals("NULL", StringComparison.OrdinalIgnoreCase))
        {
            return !row.TryGetValue(key, out var v) || v is null || v is DBNull;
        }

        // Handle IS SOME (parts: key, IS, SOME)
        if (op.Equals("IS", StringComparison.OrdinalIgnoreCase)
            && parts.Length >= 3
            && parts[2].Trim().Equals("SOME", StringComparison.OrdinalIgnoreCase))
        {
            return row.TryGetValue(key, out var v) && v is not null and not DBNull;
        }

        // Handle IS NONE (parts: key, IS, NONE)
        if (op.Equals("IS", StringComparison.OrdinalIgnoreCase)
            && parts.Length >= 3
            && parts[2].Trim().Equals("NONE", StringComparison.OrdinalIgnoreCase))
        {
            return !row.TryGetValue(key, out var v) || v is null or DBNull;
        }

        var value = parts[2].Trim().Trim('\'');

        if (value.Equals("NULL", StringComparison.OrdinalIgnoreCase))
        {
            value = null;
        }

        if (!row.ContainsKey(key))
        {
            return false;
        }

        var rowValue = row[key];
        return EvaluateOperator(rowValue, op, value);
    }

    /// <summary>
    /// Evaluates a complex condition (multiple comparisons).
    /// </summary>
    private static bool EvaluateComplexCondition(Dictionary<string, object> row, string[] parts)
    {
        // Parse sequence: expr (AND|OR expr)*
        bool? current = null;
        string? pendingLogic = null; // AND or OR
        int i = 0;
        while (i + 2 < parts.Length)
        {
            var key = parts[i].Trim();
            var op = parts[i + 1].Trim();

            bool expr;
            int consumed;

            // Handle IS NOT NULL (4 tokens: key IS NOT NULL)
            if (op.Equals("IS", StringComparison.OrdinalIgnoreCase)
                && i + 3 < parts.Length
                && parts[i + 2].Trim().Equals("NOT", StringComparison.OrdinalIgnoreCase)
                && parts[i + 3].Trim().Equals("NULL", StringComparison.OrdinalIgnoreCase))
            {
                expr = row.TryGetValue(key, out var v) && v is not null && v is not DBNull;
                consumed = 4;
            }
            // Handle IS NULL (3 tokens: key IS NULL)
            else if (op.Equals("IS", StringComparison.OrdinalIgnoreCase)
                     && parts[i + 2].Trim().Equals("NULL", StringComparison.OrdinalIgnoreCase))
            {
                expr = !row.TryGetValue(key, out var v) || v is null || v is DBNull;
                consumed = 3;
            }
            // Handle IS SOME (3 tokens: key IS SOME)
            else if (op.Equals("IS", StringComparison.OrdinalIgnoreCase)
                     && parts[i + 2].Trim().Equals("SOME", StringComparison.OrdinalIgnoreCase))
            {
                expr = row.TryGetValue(key, out var v) && v is not null and not DBNull;
                consumed = 3;
            }
            // Handle IS NONE (3 tokens: key IS NONE)
            else if (op.Equals("IS", StringComparison.OrdinalIgnoreCase)
                     && parts[i + 2].Trim().Equals("NONE", StringComparison.OrdinalIgnoreCase))
            {
                expr = !row.TryGetValue(key, out var v) || v is null or DBNull;
                consumed = 3;
            }
            else
            {
                // ✅ Parity (BETWEEN): "price BETWEEN 10 AND 16" -> 5 tokens: key BETWEEN low AND high
                if (op.Equals("BETWEEN", StringComparison.OrdinalIgnoreCase) && i + 4 < parts.Length)
                {
                    var low = parts[i + 2].Trim().Trim('\'');
                    var high = parts[i + 4].Trim().Trim('\'');
                    expr = row.ContainsKey(key) && EvaluateBetween(row, key, low, high);
                    consumed = 5;
                }
                else
                {
                    var value = parts[i + 2].Trim().Trim('\'');

                    if (value.Equals("NULL", StringComparison.OrdinalIgnoreCase))
                    {
                        value = null;
                    }

                    expr = false;
                    if (row.ContainsKey(key))
                    {
                        var rowValue = row[key];
                        expr = EvaluateOperator(rowValue, op, value);
                    }
                    consumed = 3;
                }
            }

            if (current is null)
            {
                current = expr;
            }
            else if (pendingLogic == "AND")
            {
                current = current.Value && expr;
            }
            else if (pendingLogic == "OR")
            {
                current = current.Value || expr;
            }

            // Move index to next token after this expression
            i += consumed;
            // Read logical connector if present
            if (i < parts.Length)
            {
                var maybeLogic = parts[i].Trim().ToUpperInvariant();
                if (maybeLogic == "AND" || maybeLogic == "OR")
                {
                    pendingLogic = maybeLogic;
                    i += 1;
                }
                else
                {
                    pendingLogic = null;
                }
            }
        }

        return current ?? false;
    }

    /// <summary>
    /// Compares two string values using the specified collation.
    /// ✅ COLLATE Phase 3: Hot path — uses Span-based comparison for NOCASE to avoid allocations.
    /// </summary>
    /// <param name="left">Left string to compare.</param>
    /// <param name="right">Right string to compare.</param>
    /// <param name="collation">Collation type to use.</param>
    /// <returns>
    /// &lt; 0 if left is less than right,
    /// 0 if equal,
    /// &gt; 0 if left is greater than right.
    /// </returns>
    private static int CompareWithCollation(ReadOnlySpan<char> left, ReadOnlySpan<char> right, CollationType collation)
    {
        return collation switch
        {
            CollationType.Binary => left.SequenceCompareTo(right),
            CollationType.NoCase => left.CompareTo(right, StringComparison.OrdinalIgnoreCase),
            CollationType.RTrim => left.TrimEnd().SequenceCompareTo(right.TrimEnd()),
            CollationType.UnicodeCaseInsensitive 
                => left.CompareTo(right, StringComparison.CurrentCultureIgnoreCase),
            _ => left.SequenceCompareTo(right),
        };
    }

    /// <summary>
    /// Equality comparison using collation rules.
    /// ✅ COLLATE Phase 3: Used by WHERE clause evaluation.
    /// </summary>
    /// <param name="left">Left string.</param>
    /// <param name="right">Right string.</param>
    /// <param name="collation">Collation type.</param>
    /// <returns>True if equal according to collation rules.</returns>
    private static bool EqualsWithCollation(string? left, string? right, CollationType collation)
    {
        if (left is null || right is null)
            return left == right;

        return collation switch
        {
            CollationType.Binary => left.Equals(right, StringComparison.Ordinal),
            CollationType.NoCase => left.Equals(right, StringComparison.OrdinalIgnoreCase),
            CollationType.RTrim => left.TrimEnd().Equals(right.TrimEnd(), StringComparison.Ordinal),
            CollationType.UnicodeCaseInsensitive 
                => left.Equals(right, StringComparison.CurrentCultureIgnoreCase),
            _ => left.Equals(right, StringComparison.Ordinal),
        };
    }

    /// <summary>
    /// Evaluates an operator comparison.
    /// ✅ COLLATE Phase 3: Now supports collation-aware string comparisons.
    /// OPTIMIZED: Cache ToString() result to avoid repeated conversions.
    /// </summary>
    /// <param name="rowValue">The row value to compare.</param>
    /// <param name="op">The comparison operator (=, !=, &lt;, &lt;=, &gt;, &gt;=, LIKE, IN, etc.).</param>
    /// <param name="value">The value to compare against.</param>
    /// <param name="collation">Optional collation type for string comparisons. Defaults to Binary (case-sensitive).</param>
    /// <returns>True if the comparison evaluates to true, false otherwise.</returns>
    private static bool EvaluateOperator(object? rowValue, string op, string? value, CollationType collation = CollationType.Binary)
    {
        // Convert types for accurate numeric/date comparisons
        object? rhs = value;
        if (rowValue is not null && value is not null)
        {
            try
            {
                var targetType = rowValue.GetType();
                if (targetType == typeof(int)) rhs = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                else if (targetType == typeof(long)) rhs = long.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                else if (targetType == typeof(double)) rhs = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                else if (targetType == typeof(decimal)) rhs = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                else if (targetType == typeof(bool)) rhs = (value.Equals("1") || value.Equals("true", StringComparison.OrdinalIgnoreCase));
                else if (targetType == typeof(DateTime)) rhs = DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch
            {
                // Fallback to string compare
                rhs = value;
            }
        }
        
        // ✅ COLLATE Phase 3: Collation-aware comparison function
        int Compare(object? a, object? b)
        {
            if (a is IComparable ca && b is not null && a.GetType() == b.GetType())
                return ca.CompareTo(b);
            
            // Fallback to string comparison with collation
            var sa = a?.ToString();
            var sb = b?.ToString();
            
            if (sa is null || sb is null)
                return string.Compare(sa, sb, StringComparison.Ordinal);
            
            return CompareWithCollation(sa.AsSpan(), sb.AsSpan(), collation);
        }
        
        string? rowValueStr = rowValue?.ToString();
        
        return op switch
        {
            // ✅ COLLATE Phase 3: Use collation-aware equality
            "=" => EqualsWithCollation(rowValueStr, value, collation),
            "!=" => !EqualsWithCollation(rowValueStr, value, collation),
            "<" => Compare(rowValue, rhs) < 0,
            "<=" => Compare(rowValue, rhs) <= 0,
            ">" => Compare(rowValue, rhs) > 0,
            ">=" => Compare(rowValue, rhs) >= 0,
            // ✅ Parity: NULL matches nothing (SQL) and %/_ patterns are honored
            "LIKE" => rowValueStr is not null && value is not null && LikeMatch(rowValueStr, value),
            "NOT LIKE" => rowValueStr is not null && value is not null && !LikeMatch(rowValueStr, value),
            "REGEXP" => rowValueStr is not null && value is not null && Regex.IsMatch(rowValueStr, value, RegexOptions.None, TimeSpan.FromSeconds(1)),
            "NOT REGEXP" => rowValueStr is null || value is null || !Regex.IsMatch(rowValueStr, value, RegexOptions.None, TimeSpan.FromSeconds(1)),
            "IN" => SqlInPredicate.ValueInList(rowValueStr, value),
            "NOT IN" => !SqlInPredicate.ValueInList(rowValueStr, value),
            _ => throw new InvalidOperationException($"Unsupported operator {op}"),
        };
    }

    /// <summary>
    /// Evaluates a BETWEEN condition: key >= low AND key <= high (numeric or string).
    /// ✅ Parity with single-file mode.
    /// </summary>
    private static bool EvaluateBetween(Dictionary<string, object> row, string key, string low, string high)
    {
        if (!row.TryGetValue(key, out var rowVal) || rowVal is null or DBNull)
        {
            return false;
        }

        // ✅ Parity (culture-neutral): numeric comparison. Do NOT round-trip through
        // ToString() + InvariantCulture parse — on e.g. nl-NL, 10.5.ToString() yields
        // "10,5" which fails InvariantCulture parsing and breaks BETWEEN. Converting the
        // boxed numeric value directly is culture-independent and matches SQL semantics.
        if (rowVal is not string)
        {
            double valD = 0;
            double lowD = 0;
            double highD = 0;
            bool numeric = false;
            try
            {
                valD = Convert.ToDouble(rowVal, System.Globalization.CultureInfo.InvariantCulture);
                lowD = Convert.ToDouble(low, System.Globalization.CultureInfo.InvariantCulture);
                highD = Convert.ToDouble(high, System.Globalization.CultureInfo.InvariantCulture);
                numeric = true;
            }
            catch
            {
                numeric = false;
            }

            if (numeric)
            {
                return valD >= lowD && valD <= highD;
            }
        }

        // Fallback: string comparison
        return string.CompareOrdinal(rowVal.ToString(), low) >= 0 &&
               string.CompareOrdinal(rowVal.ToString(), high) <= 0;
    }

    /// <summary>
    /// Matches a SQL LIKE pattern (% = any sequence, _ = single char), case-insensitive.
    /// </summary>
    private static bool LikeMatch(string input, string pattern)
    {
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("%", ".*").Replace("_", ".") + "$";
        return Regex.IsMatch(input, regex, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// Binds parameters to a SQL query string, replacing placeholders with actual values.
    /// Supports both named parameters (@paramName) and positional parameters (?).
    /// Delegates to <see cref="ParameterBinder.Bind"/> (position-aware, token-based), the
    /// single source of truth for parameter binding.
    /// </summary>
    private static string BindParameters(string sql, Dictionary<string, object?> parameters)
    {
        return ParameterBinder.Bind(sql, parameters);
    }

    /// <summary>
    /// Performs basic SQL sanitization by escaping single quotes.
    /// WARNING: This is NOT sufficient for preventing SQL injection. Always use parameterized queries.
    /// </summary>
    private static string SanitizeSql(string sql)
    {
        return sql.Replace("'", "''");
    }

    /// <summary>
    /// Parses a COLLATE clause and returns (CollationType, localeName) tuple.
    /// ✅ Phase 9: Supports LOCALE("xx_XX") syntax for culture-specific collations.
    /// Examples:
    /// - "BINARY" → (Binary, null)
    /// - "NOCASE" → (NoCase, null)
    /// - "LOCALE(\"tr_TR\")" → (Locale, "tr_TR")
    /// </summary>
    /// <param name="collateSpec">The remainder after COLLATE keyword.</param>
    /// <returns>Tuple of (CollationType, localeName). localeName is non-null only for Locale type.</returns>
    /// <exception cref="InvalidOperationException">If collation syntax is invalid.</exception>
    private static (CollationType, string?) ParseCollationSpec(string collateSpec)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collateSpec);

        var spec = collateSpec.Trim();

        // Check for LOCALE("xx_XX") syntax
        if (spec.StartsWith("LOCALE", StringComparison.OrdinalIgnoreCase))
        {
            // Extract locale name from LOCALE("xx_XX")
            var localeStart = spec.IndexOf('(');
            var localeEnd = spec.IndexOf(')');

            if (localeStart < 0 || localeEnd < 0 || localeEnd <= localeStart)
            {
                throw new InvalidOperationException(
                    $"Invalid LOCALE syntax. Expected: LOCALE(\"locale_name\"), got: {spec}");
            }

            var localeContent = spec.Substring(localeStart + 1, localeEnd - localeStart - 1).Trim();
            var isQuoted = localeContent.Length >= 2 &&
                ((localeContent[0] == '"' && localeContent[^1] == '"') ||
                 (localeContent[0] == '\'' && localeContent[^1] == '\''));

            if (!isQuoted)
            {
                throw new InvalidOperationException(
                    $"LOCALE collation requires a quoted locale name. Expected: LOCALE(\"locale_name\"), got: {spec}");
            }

            // Remove quotes around locale name
            var localeName = localeContent[1..^1].Trim();

            if (string.IsNullOrWhiteSpace(localeName))
            {
                throw new InvalidOperationException(
                    $"LOCALE collation requires a non-empty locale name. Got: {spec}");
            }

            // Validate locale name by attempting to create a CultureInfo
            try
            {
                _ = CultureInfoCollation.Instance.GetCulture(localeName);
                return (CollationType.Locale, localeName);
            }
            catch (Exception ex) when (ex is ArgumentException or System.Globalization.CultureNotFoundException)
            {
                throw new InvalidOperationException(
                    $"Invalid locale name '{localeName}' in COLLATE clause. Ensure it's a valid culture identifier (e.g., 'en_US', 'de_DE', 'tr_TR'). " +
                    $"Error: {ex.Message}", ex);
            }
        }

        // Built-in collations (no locale name)
        return spec.ToUpperInvariant() switch
        {
            "BINARY" => (CollationType.Binary, null),
            "NOCASE" => (CollationType.NoCase, null),
            "RTRIM" => (CollationType.RTrim, null),
            "UNICODE_CI" => (CollationType.UnicodeCaseInsensitive, null),
            _ => throw new InvalidOperationException(
                $"Unknown collation type: {spec}. Supported: BINARY, NOCASE, RTRIM, UNICODE_CI, LOCALE(\"locale_name\")")
        };
    }
}
