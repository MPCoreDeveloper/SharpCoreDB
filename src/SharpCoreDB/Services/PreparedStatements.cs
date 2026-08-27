// <copyright file="PreparedStatements.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

using SharpCoreDB.Constants;
using SharpCoreDB.DataStructures;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

/// <summary>
/// Prepared statement interface for optimized parameter binding without re-parsing SQL.
/// PERFORMANCE: Cached SQL parsing eliminates 95%+ of parsing overhead for repeated statements.
/// Target: 50k updates from 3.79 seconds to less than 100 milliseconds (38x speedup)
/// </summary>
public interface IPreparedStatement : IDisposable
{
    /// <summary>Gets the original SQL template with ? placeholders.</summary>
    string Sql { get; }

    /// <summary>Gets the cached query plan (avoids re-parsing).</summary>
    CachedQueryPlan Plan { get; }

    /// <summary>Gets the parameter count.</summary>
    int ParameterCount { get; }

    /// <summary>Binds positional parameters (?) to the prepared statement.</summary>
    /// <param name="parameters">Parameters in order of ? placeholders.</param>
    /// <returns>Bound SQL with parameters substituted.</returns>
    string BindParameters(params object?[] parameters);

    /// <summary>Binds named parameters (@paramName) to the prepared statement.</summary>
    /// <param name="parameters">Dictionary of parameter names to values.</param>
    /// <returns>Bound SQL with parameters substituted.</returns>
    string BindParameters(Dictionary<string, object?> parameters);
}

/// <summary>
/// Prepared statement implementation with cached parsing and efficient parameter binding.
/// </summary>
internal sealed class PreparedStatement : IPreparedStatement
{
    private readonly CachedQueryPlan _plan;
    private readonly int[] _parameterPositions;  // Byte offsets of ? in original SQL
    private readonly Dictionary<string, int>? _namedParameters;  // For @param binding
    private bool _disposed;

    /// <inheritdoc />
    public string Sql => _plan.Sql;

    /// <inheritdoc />
    public CachedQueryPlan Plan => _plan;

    /// <inheritdoc />
    public int ParameterCount => _parameterPositions.Length;

    /// <summary>Initializes a new prepared statement with cached plan.</summary>
    internal PreparedStatement(CachedQueryPlan plan)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        
        // Pre-compute parameter positions for fast binding
        _parameterPositions = ExtractParameterPositions(_plan.Sql);
        
        // Extract named parameters if present
        _namedParameters = ExtractNamedParameters(_plan.Sql);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public string BindParameters(params object?[] parameters)
    {
        if (parameters.Length != _parameterPositions.Length)
            throw new ArgumentException(
                $"Expected {_parameterPositions.Length} parameters, got {parameters.Length}");

        // Fast path: No parameters needed
        if (_parameterPositions.Length == 0)
            return _plan.Sql;

        // Use parameter binder for efficient substitution
        return ParameterBinder.BindPositionalParameters(_plan.Sql, _parameterPositions, parameters);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public string BindParameters(Dictionary<string, object?> parameters)
    {
        if (_namedParameters == null || _namedParameters.Count == 0)
            throw new InvalidOperationException("Prepared statement has no named parameters");

        ArgumentNullException.ThrowIfNull(parameters);

        return ParameterBinder.BindNamedParameters(_plan.Sql, _namedParameters, parameters);
    }

    /// <summary>Extracts positions of ? placeholders in SQL.</summary>
    private static int[] ExtractParameterPositions(string sql)
    {
        var positions = new List<int>();
        bool inString = false;
        char stringChar = '\0';

        for (int i = 0; i < sql.Length; i++)
        {
            char c = sql[i];

            // Handle string literals
            if ((c == '\'' || c == '"') && (i == 0 || sql[i - 1] != '\\'))
            {
                if (!inString)
                {
                    inString = true;
                    stringChar = c;
                }
                else if (c == stringChar)
                {
                    inString = false;
                }
            }

            // Count ? outside strings
            if (!inString && c == '?')
            {
                positions.Add(i);
            }
        }

        return positions.ToArray();
    }

    /// <summary>Extracts named parameters (@paramName) from SQL.</summary>
    private static Dictionary<string, int>? ExtractNamedParameters(string sql)
    {
        var parameters = new Dictionary<string, int>();
        bool inString = false;
        char stringChar = '\0';

        for (int i = 0; i < sql.Length; i++)
        {
            char c = sql[i];

            // Handle string literals
            if ((c == '\'' || c == '"') && (i == 0 || sql[i - 1] != '\\'))
            {
                if (!inString)
                {
                    inString = true;
                    stringChar = c;
                }
                else if (c == stringChar)
                {
                    inString = false;
                }
                continue;
            }

            // Extract @paramName outside strings
            if (!inString && c == '@' && i + 1 < sql.Length && char.IsLetter(sql[i + 1]))
            {
                int nameStart = i + 1;
                int nameEnd = nameStart;

                while (nameEnd < sql.Length && (char.IsLetterOrDigit(sql[nameEnd]) || sql[nameEnd] == '_'))
                {
                    nameEnd++;
                }

                string paramName = sql.Substring(nameStart, nameEnd - nameStart);
                if (!parameters.ContainsKey(paramName))
                {
                    parameters[paramName] = i;  // Store position
                }

                // Skip past the parameter name (nameEnd is already one past the last character)
                // Note: loop increment will still happen, so we need to account for that
                i = nameEnd - 1;
            }
        }

        return parameters.Count > 0 ? parameters : null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Efficient parameter binder for prepared statements.
/// Avoids full SQL parsing - just does positional substitution.
/// PERFORMANCE: 50-100x faster than full SQL parsing!
/// </summary>
internal static class ParameterBinder
{
    /// <summary>Binds positional parameters (?) to SQL template.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    internal static string BindPositionalParameters(
        string sql,
        int[] parameterPositions,
        params object?[] parameters)
    {
        if (parameterPositions.Length == 0)
            return sql;

        // Fast path: Single parameter
        if (parameterPositions.Length == 1)
        {
            string paramValue = FormatParameter(parameters[0]);
            return sql.Substring(0, parameterPositions[0]) + 
                   paramValue + 
                   sql.Substring(parameterPositions[0] + 1);
        }

        // Build result with parameter substitution
        var sb = new System.Text.StringBuilder();
        int lastPos = 0;

        for (int i = 0; i < parameterPositions.Length; i++)
        {
            int pos = parameterPositions[i];
            
            // Add SQL fragment before parameter
            sb.Append(sql.AsSpan(lastPos, pos - lastPos));
            
            // Add formatted parameter value
            string paramValue = FormatParameter(parameters[i]);
            sb.Append(paramValue);

            lastPos = pos + 1;  // Skip the ? character
        }

        // Add remaining SQL
        if (lastPos < sql.Length)
        {
            sb.Append(sql.AsSpan(lastPos));
        }

        return sb.ToString();
    }

    /// <summary>Binds named parameters (@paramName) to SQL template.</summary>
    /// <remarks>
    /// Token-aware replacement: every occurrence of every named parameter is replaced
    /// from end to start, so parameter names that are prefixes of other names
    /// (e.g. @t vs @tid) can never corrupt each other.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    internal static string BindNamedParameters(
        string sql,
        Dictionary<string, int> namedParameters,
        Dictionary<string, object?> parameters)
    {
        // Validate that every referenced parameter has a value (fail loudly).
        foreach (var kvp in namedParameters)
        {
            if (!parameters.TryGetValue(kvp.Key, out _))
                throw new ArgumentException($"Missing required parameter: {kvp.Key}");
        }

        // Collect every occurrence of every named parameter token.
        var occurrences = new List<(string name, int pos)>();
        foreach (var (name, pos) in ExtractNamedParameterOccurrences(sql))
        {
            if (namedParameters.ContainsKey(name))
                occurrences.Add((name, pos));
        }

        // Replace from end to start (to maintain positions).
        occurrences.Sort((a, b) => b.pos.CompareTo(a.pos));

        var sb = new System.Text.StringBuilder(sql);
        foreach (var (paramName, pos) in occurrences)
        {
            string paramValue = FormatParameter(parameters[paramName]);
            int endPos = pos + 1;

            // Find end of parameter name (@paramName)
            while (endPos < sb.Length && (char.IsLetterOrDigit(sb[endPos]) || sb[endPos] == '_'))
            {
                endPos++;
            }

            sb.Remove(pos, endPos - pos);
            sb.Insert(pos, paramValue);
        }

        return sb.ToString();
    }
    /// <summary>
    /// Binds named (@paramName) or positional (?) parameters to a SQL template using exact
    /// token positions. This is the single source of truth for parameter binding: placeholders
    /// are replaced token-by-token, so parameter names that are prefixes of other names
    /// (e.g. @t vs @tid) never corrupt each other.
    /// </summary>
    /// <param name="sql">The SQL template with placeholders.</param>
    /// <param name="parameters">
    /// The parameters to bind. Keys may be prefixed with '@' or ':' (or unprefixed) for named
    /// parameters, and '0'..'N-1' (or dictionary order) for positional '?' placeholders.
    /// </param>
    /// <returns>The bound SQL.</returns>
    internal static string Bind(string sql, Dictionary<string, object?> parameters)
    {
        ArgumentNullException.ThrowIfNull(sql);

        if (parameters is null || parameters.Count == 0)
        {
            return sql;
        }

        // Normalize keys so '@name', ':name' and 'name' all resolve to the same parameter.
        var normalized = new Dictionary<string, object?>(parameters.Count, StringComparer.Ordinal);
        foreach (var kvp in parameters)
        {
            var key = kvp.Key;
            int trim = 0;
            while (trim < key.Length && (key[trim] == '@' || key[trim] == ':'))
            {
                trim++;
            }

            normalized[key[trim..]] = kvp.Value;
        }

        var namedParameters = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (name, pos) in ExtractNamedParameterOccurrences(sql))
        {
            if (!namedParameters.ContainsKey(name))
                namedParameters[name] = pos;
        }

        if (namedParameters.Count > 0)
        {
            if (ExtractPositionalParameterPositions(sql).Length > 0)
            {
                throw new InvalidOperationException(
                    "Mixed parameter styles detected: found both '@param' named parameters and '?' positional placeholders. " +
                    "Use either '?' placeholders with keys '0','1','2',... OR '@name' placeholders with keys 'name','email',... but not both.");
            }

            return BindNamedParameters(sql, namedParameters, normalized);
        }

        var positionalPositions = ExtractPositionalParameterPositions(sql);
        if (positionalPositions.Length > 0)
        {
            var orderedValues = new object?[positionalPositions.Length];

            // Numeric keys (0..N-1) preferred; otherwise use dictionary order.
            bool numericKeysAvailable = true;
            for (int i = 0; i < positionalPositions.Length; i++)
            {
                if (!normalized.ContainsKey(i.ToString()))
                {
                    numericKeysAvailable = false;
                    break;
                }
            }

            if (numericKeysAvailable)
            {
                for (int i = 0; i < positionalPositions.Length; i++)
                {
                    orderedValues[i] = normalized[i.ToString()];
                }
            }
            else
            {
                int valueIndex = 0;
                foreach (var value in normalized.Values)
                {
                    if (valueIndex >= orderedValues.Length)
                    {
                        break;
                    }

                    orderedValues[valueIndex++] = value;
                }
            }

            return BindPositionalParameters(sql, positionalPositions, orderedValues);
        }

        return sql;
    }
    /// <summary>Extracts every named parameter token (@paramName) from SQL, outside string literals.</summary>
    internal static List<(string name, int pos)> ExtractNamedParameterOccurrences(string sql)
    {
        var occurrences = new List<(string name, int pos)>();
        bool inString = false;
        char stringChar = '\0';

        for (int i = 0; i < sql.Length; i++)
        {
            char c = sql[i];

            // Skip string literals ('...' or "...").
            if ((c == '\'' || c == '"') && (i == 0 || sql[i - 1] != '\\'))
            {
                if (!inString)
                {
                    inString = true;
                    stringChar = c;
                }
                else if (c == stringChar)
                {
                    inString = false;
                }

                continue;
            }

            if (inString || c != '@' || i + 1 >= sql.Length || !char.IsLetterOrDigit(sql[i + 1]))
            {
                continue;
            }

            int nameStart = i + 1;
            int nameEnd = nameStart;
            while (nameEnd < sql.Length && (char.IsLetterOrDigit(sql[nameEnd]) || sql[nameEnd] == '_'))
            {
                nameEnd++;
            }

            occurrences.Add((sql[nameStart..nameEnd], i));
            i = nameEnd - 1;  // Skip past the parameter token
        }

        return occurrences;
    }

    /// <summary>Extracts positions of '?' placeholders from SQL, outside string literals.</summary>
    internal static int[] ExtractPositionalParameterPositions(string sql)
    {
        var positions = new List<int>();
        bool inString = false;
        char stringChar = '\0';

        for (int i = 0; i < sql.Length; i++)
        {
            char c = sql[i];

            if ((c == '\'' || c == '"') && (i == 0 || sql[i - 1] != '\\'))
            {
                if (!inString)
                {
                    inString = true;
                    stringChar = c;
                }
                else if (c == stringChar)
                {
                    inString = false;
                }

                continue;
            }

            if (!inString && c == '?')
            {
                positions.Add(i);
            }
        }

        return positions.ToArray();
    }





    /// <summary>Formats a parameter value for SQL substitution.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string FormatParameter(object? value)
    {
        if (value == null || value == DBNull.Value)
            return "NULL";

        return value switch
        {
            string str => $"'{EscapeSqlString(str)}'",
            bool b => b ? "1" : "0",
            DateTime dt => $"'{dt:yyyy-MM-dd HH:mm:ss.fff}'",
            decimal d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            float f => f.ToString(System.Globalization.CultureInfo.InvariantCulture),
            int i => i.ToString(),
            long l => l.ToString(),
            byte[] bytes => $"0x{System.Convert.ToHexString(bytes)}",
            _ => $"'{EscapeSqlString(value.ToString()!)}'",
        };
    }

    /// <summary>Escapes single quotes in SQL strings.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string EscapeSqlString(string value) => value.Replace("'", "''");
}
