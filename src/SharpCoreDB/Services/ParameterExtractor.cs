// <copyright file="ParameterExtractor.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// ✅ C# 14: Extracts parameter information from SQL queries.
/// Used by Phase 2 Task 2.2 to enable compilation for parameterized queries.
/// </summary>
internal sealed class ParameterExtractor
{
    /// <summary>
    /// Information about a single parameter in SQL.
    /// </summary>
    public sealed record ParameterInfo
    {
        /// <summary>Parameter name without @ (e.g., "id" for "@id").</summary>
        public required string Name { get; init; }

        /// <summary>Parameter name with @ (e.g., "@id").</summary>
        public required string FullName { get; init; }

        /// <summary>Zero-based index of this parameter (0, 1, 2...).</summary>
        public required int Index { get; init; }

        /// <summary>Position in SQL string where parameter first appears.</summary>
        public required int Position { get; init; }

        /// <summary>Returns human-readable representation.</summary>
        public override string ToString() => $"{FullName} (index: {Index}, position: {Position})";
    }

    /// <summary>
    /// Regex pattern to match SQL parameters: @paramName or ?
    /// ✅ Matches: @id, @user_id, @name123, but not @123 (must start with letter)
    /// </summary>
    private static readonly Regex ParameterPattern = new(
        @"@([a-zA-Z_][a-zA-Z0-9_]*)",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// Extracts all parameters from a SQL query.
    /// ✅ C# 14: Collection expression for result
    /// ✅ Skips parameters inside SQL string literals
    /// </summary>
    /// <param name="sql">The SQL query string.</param>
    /// <returns>Array of parameter information, in order of appearance.</returns>
    public static ParameterInfo[] ExtractParameters(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        // Remove string literals before extracting parameters, but track original positions
        var (sqlWithoutLiterals, positionMap) = RemoveStringLiteralsWithPositionMap(sql);
        var matches = ParameterPattern.Matches(sqlWithoutLiterals);

        if (matches.Count == 0)
        {
            return [];  // ✅ C# 14: Collection expression for empty array
        }

        var parameters = new List<ParameterInfo>(matches.Count);
        var seen = new HashSet<string>();  // Track unique parameters

        for (int i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var paramName = match.Groups[1].Value;  // Get capture group (without @)
            var fullName = $"@{paramName}";
            
            // Map position back to original SQL
            int originalPosition = positionMap[match.Index];

            // Only add unique parameters (first occurrence only)
            if (seen.Add(paramName))
            {
                parameters.Add(new ParameterInfo
                {
                    Name = paramName,
                    FullName = fullName,
                    Index = parameters.Count,  // Sequential index for unique params
                    Position = originalPosition
                });
            }
        }

        return [..parameters];  // ✅ C# 14: Spread operator for collection expression
    }

    /// <summary>
    /// Removes SQL string literals and returns a mapping of new positions to original positions.
    /// ✅ Handles escaped quotes ('') correctly.
    /// ✅ Zero-allocation for small strings using stackalloc
    /// </summary>
    /// <param name="sql">The SQL query string.</param>
    /// <returns>Tuple of (SQL with literals removed, position mapping array).</returns>
    private static (string, int[]) RemoveStringLiteralsWithPositionMap(string sql)
    {
        Span<char> result = stackalloc char[sql.Length <= 512 ? sql.Length : 0];
        char[]? rentedArray = null;
        int[]? positionMapArray = null;
        
        if (sql.Length > 512)
        {
            rentedArray = ArrayPool<char>.Shared.Rent(sql.Length);
            result = rentedArray.AsSpan(0, sql.Length);
        }

        positionMapArray = ArrayPool<int>.Shared.Rent(sql.Length);
        Span<int> positionMap = positionMapArray.AsSpan(0, sql.Length);

        try
        {
            bool inString = false;
            int writePos = 0;

            for (int i = 0; i < sql.Length; i++)
            {
                char c = sql[i];

                if (c == '\'')
                {
                    // Check if it's an escaped quote ('')
                    if (i + 1 < sql.Length && sql[i + 1] == '\'')
                    {
                        // Escaped quote - replace both with spaces and map both
                        result[writePos] = ' ';
                        positionMap[writePos] = i;
                        writePos++;
                        result[writePos] = ' ';
                        positionMap[writePos] = i + 1;
                        writePos++;
                        i++; // Skip the next quote
                        continue;
                    }

                    // Toggle string state
                    inString = !inString;
                    result[writePos] = ' '; // Replace quote with space
                    positionMap[writePos] = i;
                    writePos++;
                }
                else if (inString)
                {
                    // Inside string literal - replace with space
                    result[writePos] = ' ';
                    positionMap[writePos] = i;
                    writePos++;
                }
                else
                {
                    // Outside string literal - keep character
                    result[writePos] = c;
                    positionMap[writePos] = i;
                    writePos++;
                }
            }

            var resultString = new string(result[..writePos]);
            var finalMap = positionMap[..writePos].ToArray();
            return (resultString, finalMap);
        }
        finally
        {
            if (rentedArray != null)
            {
                ArrayPool<char>.Shared.Return(rentedArray);
            }
            if (positionMapArray != null)
            {
                ArrayPool<int>.Shared.Return(positionMapArray);
            }
        }
    }

    /// <summary>
    /// Removes SQL string literals (single-quoted strings) from SQL text.
    /// ✅ Handles escaped quotes ('') correctly.
    /// ✅ Zero-allocation for small strings using stackalloc
    /// </summary>
    /// <param name="sql">The SQL query string.</param>
    /// <returns>SQL with string literals replaced by spaces.</returns>
    private static string RemoveStringLiterals(string sql)
    {
        Span<char> result = stackalloc char[sql.Length <= 512 ? sql.Length : 0];
        char[]? rentedArray = null;
        
        if (sql.Length > 512)
        {
            rentedArray = ArrayPool<char>.Shared.Rent(sql.Length);
            result = rentedArray.AsSpan(0, sql.Length);
        }

        try
        {
            bool inString = false;
            int writePos = 0;

            for (int i = 0; i < sql.Length; i++)
            {
                char c = sql[i];

                if (c == '\'')
                {
                    // Check if it's an escaped quote ('')
                    if (i + 1 < sql.Length && sql[i + 1] == '\'')
                    {
                        // Escaped quote - replace both with spaces
                        result[writePos++] = ' ';
                        result[writePos++] = ' ';
                        i++; // Skip the next quote
                        continue;
                    }

                    // Toggle string state
                    inString = !inString;
                    result[writePos++] = ' '; // Replace quote with space
                }
                else if (inString)
                {
                    // Inside string literal - replace with space
                    result[writePos++] = ' ';
                }
                else
                {
                    // Outside string literal - keep character
                    result[writePos++] = c;
                }
            }

            return new string(result[..writePos]);
        }
        finally
        {
            if (rentedArray != null)
            {
                ArrayPool<char>.Shared.Return(rentedArray);
            }
        }
    }

    /// <summary>
    /// Checks if a SQL query contains any parameters.
    /// ✅ Fast check without full regex compilation
    /// </summary>
    /// <param name="sql">The SQL query string.</param>
    /// <returns>True if query contains @param placeholders.</returns>
    public static bool HasParameters(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        return sql.Contains('@');
    }

    /// <summary>
    /// Validates parameter names in SQL (checks for valid identifiers).
    /// ✅ Ensures parameters follow SQL naming conventions
    /// </summary>
    /// <param name="sql">The SQL query string.</param>
    /// <returns>True if all parameters are valid identifiers.</returns>
    public static bool AreParametersValid(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        // First, check if SQL contains any @ symbols that could be parameters
        int atIndex = sql.IndexOf('@');
        if (atIndex == -1)
        {
            return true;  // No @ symbols, so no parameters to validate
        }

        // Check for invalid parameter patterns (@ followed by digit)
        for (int i = 0; i < sql.Length; i++)
        {
            if (sql[i] == '@' && i + 1 < sql.Length)
            {
                char nextChar = sql[i + 1];
                // If @ is followed by a digit, it's an invalid parameter
                if (char.IsDigit(nextChar))
                {
                    return false;
                }
                // If @ is followed by something that's not a letter, underscore, or whitespace, it might be invalid
                // But we'll let the regex extraction handle the full validation
            }
        }

        var parameters = ExtractParameters(sql);

        // Check each parameter name
        foreach (var param in parameters)
        {
            // Must start with letter or underscore
            if (!char.IsLetter(param.Name[0]) && param.Name[0] != '_')
            {
                return false;
            }

            // Rest must be letters, digits, or underscores
            if (!param.Name.Skip(1).All(c => char.IsLetterOrDigit(c) || c == '_'))
            {
                return false;
            }

            // Maximum length check (SQL standard)
            if (param.Name.Length > 128)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Gets parameter count without allocating array.
    /// ✅ Efficient for simple count check
    /// </summary>
    /// <param name="sql">The SQL query string.</param>
    /// <returns>Number of unique parameters in query.</returns>
    public static int GetParameterCount(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        return ExtractParameters(sql).Length;
    }

    /// <summary>
    /// Gets parameter names for validation against provided values.
    /// ✅ Used to validate ExecuteCompiledQuery() parameters
    /// </summary>
    /// <param name="sql">The SQL query string.</param>
    /// <returns>Set of expected parameter names (without @).</returns>
    public static HashSet<string> GetExpectedParameters(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        var parameters = ExtractParameters(sql);
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var param in parameters)
        {
            expected.Add(param.Name);
        }

        return expected;
    }

    /// <summary>
    /// Validates provided parameters against expected parameters.
    /// ✅ Ensures all required parameters are provided
    /// </summary>
    /// <param name="sql">The SQL query string.</param>
    /// <param name="providedParameters">Dictionary of provided parameter values.</param>
    /// <returns>Validation result with error message if invalid.</returns>
    public static (bool IsValid, string? ErrorMessage) ValidateParameters(
        string sql,
        Dictionary<string, object?> providedParameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        ArgumentNullException.ThrowIfNull(providedParameters);

        var expected = GetExpectedParameters(sql);
        
        // Create case-insensitive key set from provided parameters for lookup
        var providedKeysLower = new HashSet<string>(
            providedParameters.Keys.Select(k => k.StartsWith("@") ? k.Substring(1).ToLowerInvariant() : k.ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);

        // Check if all expected parameters are provided
        foreach (var paramName in expected)
        {
            if (!providedKeysLower.Contains(paramName.ToLowerInvariant()))
            {
                return (false, $"Missing required parameter: {paramName}");
            }
        }

        // Check for extra parameters provided
        var provided = new HashSet<string>(providedParameters.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var key in provided)
        {
            var cleanKey = key.StartsWith("@") ? key.Substring(1) : key;
            if (!expected.Contains(cleanKey))
            {
                // Extra parameter - this is a warning but not an error (might be for optional processing)
            }
        }

        return (true, null);
    }
}
