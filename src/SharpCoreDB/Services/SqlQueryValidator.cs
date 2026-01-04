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
        new Regex(@"--", RegexOptions.Compiled),
        new Regex(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline),
        
        // Multiple statements (; terminator followed by another statement)
        new Regex(@";\s*(SELECT|INSERT|UPDATE|DELETE|DROP|CREATE|ALTER)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        
        // Common injection payloads
        new Regex(@"'\s*(OR|AND)\s+('|1)\s*=\s*('|1)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new Regex(@"'\s*OR\s+'[^']*'\s*=\s*'", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        
        // Union-based injection
        new Regex(@"UNION\s+(ALL\s+)?SELECT", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        
        // Stacked queries
        new Regex(@";\s*DROP\s+TABLE", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new Regex(@";\s*DELETE\s+FROM", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        
        // Time-based blind injection
        new Regex(@"(SLEEP|WAITFOR|BENCHMARK)\s*\(", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        
        // System functions/procedures
        new Regex(@"(xp_cmdshell|sp_executesql|EXEC\s*\()", RegexOptions.Compiled | RegexOptions.IgnoreCase),
    ];

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

        var warnings = new List<string>();

        // Check 1: Missing parameters for non-SELECT queries with values
        if (parameters == null || parameters.Count == 0)
        {
            // S1066 Fix: Merge nested if statement
            if (ContainsStringLiterals(sql) && !IsSafeStatement(sql))
            {
                warnings.Add("Query contains string literals but no parameters - potential SQL injection risk");
            }
        }

        // Check 2: Scan for dangerous patterns
        // S3267 Fix: Use LINQ Where to filter and iterate
        foreach (var pattern in DangerousPatterns.Where(p => p.IsMatch(sql)))
        {
            warnings.Add($"Detected potentially dangerous SQL pattern: {pattern}");
        }

        // Check 3: Look for concatenation patterns
        if (sql.Contains("'") && sql.Contains("+"))
        {
            warnings.Add("Query appears to use string concatenation - use parameterized queries instead");
        }

        // Check 4: Validate parameter placeholders match usage
        if (parameters != null && parameters.Count > 0)
        {
            // Count ? placeholders
            int placeholderCount = sql.Count(c => c == '?');
            
            // Count @param placeholders (named parameters)
            var namedMatches = System.Text.RegularExpressions.Regex.Matches(sql, @"@(\w+)");
            int namedPlaceholderCount = namedMatches.Count;
            
            if (placeholderCount > 0 && namedPlaceholderCount > 0)
            {
                warnings.Add($"Mixed parameter styles detected: {placeholderCount} '?' and {namedPlaceholderCount} '@param' placeholders");
            }
            else if (placeholderCount > 0)
            {
                // Positional parameters - keys should be "0", "1", "2", etc.
                if (placeholderCount != parameters.Count)
                {
                    warnings.Add($"Parameter count mismatch: {parameters.Count} parameters provided but {placeholderCount} placeholders found");
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
                
                // Check for missing parameters (SQL has @param but no matching key)
                var missingParams = paramNames.Where(p => !parameters.ContainsKey(p)).ToList();
                if (missingParams.Any())
                {
                    warnings.Add($"Missing parameters for placeholders: {string.Join(", ", missingParams.Select(p => $"@{p}"))}");
                }
                
                // Check for unused parameters (key provided but not in SQL)
                // Only warn if parameter count significantly exceeds SQL params (allows for flexibility)
                var unusedParams = parameters.Keys.Where(k => !paramNames.Contains(k)).ToList();
                if (unusedParams.Any() && unusedParams.Count >= paramNames.Count)
                {
                    warnings.Add($"Unused parameters provided (not in SQL): {string.Join(", ", unusedParams)}");
                }
            }
            // else: no placeholders but parameters provided - likely already bound, skip warning
        }

        // Handle warnings based on mode
        if (warnings.Any())
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
                Console.WriteLine($"⚠️  {message}");
                Console.WriteLine($"   Query: {TruncateQuery(sql)}");
            }
        }
    }

    /// <summary>
    /// Checks if a SQL statement is considered safe (DDL, simple SELECTs without user input).
    /// </summary>
    private static bool IsSafeStatement(string sql)
    {
        var trimmed = sql.Trim().ToUpperInvariant();
        
        // CREATE TABLE and other DDL statements with literals are typically safe
        if (trimmed.StartsWith("CREATE TABLE") || 
            trimmed.StartsWith("CREATE INDEX") ||
            trimmed.StartsWith("ALTER TABLE"))
        {
            return true;
        }

        // Simple SELECT * without WHERE is safe
        if (trimmed == "SELECT *" || trimmed.StartsWith("SELECT * FROM") && !trimmed.Contains("WHERE"))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if a SQL query contains string literals (potential user input).
    /// </summary>
    private static bool ContainsStringLiterals(string sql)
    {
        // Look for quoted strings
        return Regex.IsMatch(sql, @"'[^']*'");
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
