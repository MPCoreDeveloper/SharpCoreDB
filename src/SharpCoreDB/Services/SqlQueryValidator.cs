// <copyright file="SqlQueryValidator.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// Validates SQL queries for potential security vulnerabilities.
/// SECURITY: Detects SQL injection patterns and enforces parameterized query usage.
/// </summary>
public static class SqlQueryValidator
{
    // Dangerous patterns that might indicate SQL injection attempts
    private static readonly Regex[] DangerousPatterns = 
    [
        // SQL comments used to bypass authentication
        new Regex(@"--", RegexOptions.Compiled, TimeSpan.FromSeconds(1)),
        new Regex(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline, TimeSpan.FromSeconds(1)),
        
        // Multiple statements (; terminator followed by another statement)
        new Regex(@";\s*(SELECT|INSERT|UPDATE|DELETE|DROP|CREATE|ALTER)", RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)),
        
        // Common injection payloads
        new Regex(@"'\s*(OR|AND)\s+('|1)\s*=\s*('|1)", RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)),
        new Regex(@"'\s*OR\s+'[^']*'\s*=\s*'", RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)),
        
        // Union-based injection
        new Regex(@"UNION\s+(ALL\s+)?SELECT", RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)),
        
        // Stacked queries
        new Regex(@";\s*DROP\s+TABLE", RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)),
        new Regex(@";\s*DELETE\s+FROM", RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)),
        
        // Time-based blind injection
        new Regex(@"(SLEEP|WAITFOR|BENCHMARK)\s*\(", RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)),
        
        // System functions/procedures
        new Regex(@"(xp_cmdshell|sp_executesql|EXEC\s*\()", RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)),
    ];

    // Compiled regex for @param placeholder extraction (hot path for parameterized queries).
    private static readonly Regex NamedParameterRegex = new(
        @"@(\w+)", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Validation modes for SQL queries.
    /// </summary>
    public enum ValidationMode
    {
        /// <summary>
        /// Lenient mode - warnings only, no exceptions (development).
        /// </summary>
        Lenient,
        
        /// <summary>
        /// Strict mode - throws exceptions for unsafe patterns (production).
        /// </summary>
        Strict,
        
        /// <summary>
        /// Disabled - no validation (use with caution).
        /// </summary>
        Disabled
    }

    /// <summary>
    /// Validates a SQL query for security vulnerabilities.
    /// </summary>
    /// <param name="sql">The SQL query to validate.</param>
    /// <param name="parameters">The parameters being used (null if none).</param>
    /// <param name="mode">The validation mode.</param>
    /// <param name="strictParameterValidation">Whether to strictly validate named parameter keys match SQL placeholders.</param>
    /// <exception cref="SecurityException">Thrown in Strict mode if dangerous patterns detected.</exception>
    public static void ValidateQuery(string sql, Dictionary<string, object?>? parameters, ValidationMode mode = ValidationMode.Strict, bool strictParameterValidation = true)
    {
        if (mode == ValidationMode.Disabled || string.IsNullOrWhiteSpace(sql))
        {
            return;
        }

        // Allocate the warnings list lazily — the common (clean-query) path allocates nothing.
        List<string>? warnings = null;
        void Warn(string message) => (warnings ??= []).Add(message);

        // Check 1: Missing parameters for non-SELECT queries with values
        if (parameters == null || parameters.Count == 0)
        {
            // S1066 Fix: Merge nested if statement
            if (ContainsStringLiterals(sql) && !IsSafeStatement(sql))
            {
                Warn("Query contains string literals but no parameters - potential SQL injection risk");
            }
        }

        // Check 2: Scan for dangerous patterns
        // S3267 Fix: Use LINQ Where to filter and iterate
        foreach (var pattern in DangerousPatterns.Where(p => p.IsMatch(sql)))
        {
            Warn($"Detected potentially dangerous SQL pattern: {pattern}");
        }

        // Check 3: Look for concatenation patterns
        if (sql.Contains("'") && sql.Contains("+"))
        {
            Warn("Query appears to use string concatenation - use parameterized queries instead");
        }

        // Check 4: Validate parameter placeholders match usage
        if (parameters != null && parameters.Count > 0)
        {
            // Count ? placeholders
            int placeholderCount = sql.Count(c => c == '?');
            
            // Count @param placeholders (named parameters)
            var namedMatches = NamedParameterRegex.Matches(sql);
            int namedPlaceholderCount = namedMatches.Count;
            
            if (placeholderCount > 0 && namedPlaceholderCount > 0)
            {
                Warn($"Mixed parameter styles detected: {placeholderCount} '?' and {namedPlaceholderCount} '@param' placeholders");
            }
            else if (placeholderCount > 0)
            {
                // Positional parameters - keys should be "0", "1", "2", etc.
                if (placeholderCount != parameters.Count)
                {
                    Warn($"Parameter count mismatch: {parameters.Count} parameters provided but {placeholderCount} placeholders found");
                }
            }
            else if (namedPlaceholderCount > 0 && strictParameterValidation)
            {
                // Named parameters - validate keys match @param names in SQL (only if strict validation enabled)
                var paramNames = namedMatches
                    .Cast<System.Text.RegularExpressions.Match>()
                    .Select(m => m.Groups[1].Value)
                    .Distinct()
                    .ToHashSet();
                
                // ✅ FIX (Known Issue 5): Normalize caller keys by stripping the @ or : prefix,
                // EXACTLY like SqlParser.ResolveParameter does (parameterName.TrimStart('@', ':')).
                // This makes the validator consistent with the runtime: `@name` and `name` keys
                // are both accepted for the `@name` placeholder.
                static string NormalizeKey(string key) =>
                    key.Length > 1 && (key[0] == '@' || key[0] == ':')
                        ? key[1..]
                        : key;

                // Build normalized key lookup for fast membership checks.
                // A @-prefixed key and an unprefixed key with the same name are equivalent;
                // we use the first occurrence so duplicates collapse to a single normalized entry.
                var normalizedKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var key in parameters.Keys)
                {
                    normalizedKeys.Add(NormalizeKey(key));
                }
                
                // Check for missing parameters (SQL has @param but no matching key)
                // A key is missing only if neither `name`, `@name`, nor `:name` was provided.
                var missingParams = paramNames.Where(p => !normalizedKeys.Contains(p)).ToList();
                if (missingParams.Any())
                {
                    Warn($"Missing parameters for placeholders: {string.Join(", ", missingParams.Select(p => $"@{p}"))}");
                }
                
                // Check for unused parameters (key provided but not in SQL)
                // Only warn if parameter count significantly exceeds SQL params (allows for flexibility)
                // Keys are compared normalized so `@name` correctly matches the `@name` placeholder.
                var unusedParams = parameters.Keys.Where(k => !paramNames.Contains(NormalizeKey(k))).ToList();
                if (unusedParams.Any() && unusedParams.Count >= paramNames.Count)
                {
                    Warn($"Unused parameters provided (not in SQL): {string.Join(", ", unusedParams)}");
                }
            }
            // else: no placeholders but parameters provided - likely already bound, skip warning
        }


        // Handle warnings based on mode
        if (warnings is { Count: > 0 })
        {
            var message = $"SQL Security Validation Warnings:\n{string.Join("\n", warnings.Select((w, i) => $"  {i + 1}. {w}"))}";
            
            if (mode == ValidationMode.Strict)
            {
                throw new SecurityException(
                    $"{message}\n\nQuery: {TruncateQuery(sql)}\n\n" +
                    $"To fix: Use parameterized queries with ? placeholders.\n" +
                    $"Example: ExecuteSQL(\"SELECT * FROM users WHERE id = ?\", new Dictionary<string, object?> {{ {{ \"0\", userId }} }});");
            }
            else // Lenient
            {
                // ✅ FIX: Skip console output in CI environments to prevent log overflow
                // GitHub Actions sets CI=true, Azure DevOps sets TF_BUILD=true
                if (Environment.GetEnvironmentVariable("CI") is null &&
                    Environment.GetEnvironmentVariable("TF_BUILD") is null &&
                    Environment.GetEnvironmentVariable("GITHUB_ACTIONS") is null)
                {
                    Console.WriteLine($"⚠️  {message}");
                    Console.WriteLine($"   Query: {TruncateQuery(sql)}");
                }
            }
        }
    }

    /// <summary>
    /// Checks if a SQL statement is considered safe (DDL, simple SELECTs without user input).
    /// Allocation-free: case-insensitive span comparisons instead of Trim + ToUpperInvariant.
    /// </summary>
    private static bool IsSafeStatement(string sql)
    {
        var trimmed = sql.AsSpan().Trim();

        // CREATE TABLE and other DDL statements with literals are typically safe
        if (trimmed.StartsWith("CREATE TABLE", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("CREATE INDEX", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("ALTER TABLE", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Simple SELECT * without WHERE is safe
        if (trimmed.Equals("SELECT *", StringComparison.OrdinalIgnoreCase) ||
            (trimmed.StartsWith("SELECT * FROM", StringComparison.OrdinalIgnoreCase) &&
             !trimmed.Contains("WHERE", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if a SQL query contains single-quoted string literals (potential user input).
    /// Span-based scan (no regex, no allocation) with the same semantics as the previous
    /// <c>Regex.IsMatch(sql, "'[^']*'")</c>: a quote pair with any non-quote content.
    /// </summary>
    private static bool ContainsStringLiterals(string sql)
    {
        var span = sql.AsSpan();
        for (int i = 0; i < span.Length; i++)
        {
            if (span[i] == '\'')
            {
                int j = i + 1;
                while (j < span.Length && span[j] != '\'')
                {
                    j++;
                }

                if (j < span.Length)
                {
                    return true; // matching closing quote found
                }

                break; // unclosed quote — no literal
            }
        }

        return false;
    }

    /// <summary>
    /// Truncates a query for display purposes.
    /// </summary>
    private static string TruncateQuery(string sql, int maxLength = 100)
    {
        if (sql.Length <= maxLength)
        {
            return sql;
        }

        return sql.Substring(0, maxLength) + "...";
    }
}

/// <summary>
/// Exception thrown when SQL security validation fails.
/// </summary>
public class SecurityException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SecurityException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public SecurityException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SecurityException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public SecurityException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
