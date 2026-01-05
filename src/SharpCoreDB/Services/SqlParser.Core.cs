// <copyright file="SqlParser.Core.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;

/// <summary>
/// Simple SQL parser and executor - Core class with fields and interface implementation.
/// SPLIT INTO PARTIAL CLASSES FOR MAINTAINABILITY:
/// - SqlParser.Core.cs: Core class definition, fields, constructor, and public interface methods
/// - SqlParser.DDL.cs: CREATE TABLE, CREATE INDEX, DROP, ALTER operations
/// - SqlParser.DML.cs: INSERT, UPDATE, DELETE, SELECT, EXPLAIN operations
/// - SqlParser.Helpers.cs: Helper methods for parameter binding, value parsing, etc.
/// 
/// SECURITY WARNING: This parser has basic SQL injection protections but is NOT fully safe.
/// Always use parameterized queries for untrusted input. Never use string concatenation or interpolation.
/// 
/// SAFE PATTERNS:
///   - ExecuteSQL(sql, parameters) with Dictionary parameters
///   - Use @paramName or ? placeholders in SQL
/// 
/// UNSAFE PATTERNS (DO NOT USE):
///   - String interpolation: $"SELECT * FROM users WHERE name = '{userName}'"
///   - String concatenation: "DELETE FROM users WHERE id = " + userId
/// 
/// See SECURITY.md for detailed security guidelines.
/// 
/// ENHANCED PARSER:
/// This parser now includes enhanced parsing capabilities through EnhancedSqlParser:
///   - Support for RIGHT JOIN, FULL OUTER JOIN
///   - Subqueries in FROM and WHERE clauses
///   - Advanced error recovery
///   - Multiple SQL dialect support
/// Use ParseWithEnhancedParser() for complex queries.
/// </summary>
public partial class SqlParser(Dictionary<string, ITable> tables, string dbPath, IStorage storage, bool isReadOnly = false, QueryCache? queryCache = null, DatabaseConfig? config = null) : ISqlParser
{
    private readonly Dictionary<string, ITable> tables = tables;
    private readonly string dbPath = dbPath;
    private readonly IStorage storage = storage;
    private readonly bool isReadOnly = isReadOnly;
    private readonly QueryCache? queryCache = queryCache;
    private readonly DatabaseConfig? config = config;

    /// <inheritdoc />
    public void Execute(string sql, IWAL? wal = null)
    {
        this.Execute(sql, new Dictionary<string, object?>(), wal);
    }

    /// <inheritdoc />
    public void Execute(string sql, Dictionary<string, object?> parameters, IWAL? wal = null)
    {
        string? originalSql = null;
        if (parameters != null && parameters.Count > 0)
        {
            originalSql = sql;
            sql = SqlParser.BindParameters(sql, parameters);
        }
        // ✅ FIXED: Removed security warning - it was incorrectly triggering for prepared statements
        // The warning was triggering after parameter binding created a new SQL string
        // Prepared statements with parameters are SAFE and should not show warnings
        
        // Note: Sanitization still applied as defense-in-depth, but no warning logged
        else
        {
            sql = SqlParser.SanitizeSql(sql);
        }

        // Use query cache if available
        string cacheKey = originalSql ?? sql;
        string[] parts;
        
        if (this.queryCache != null)
        {
            // Cache the structure - for parameterized queries, still track cache hits
            this.queryCache.GetOrAdd(cacheKey, key =>
            {
                var parsedParts = sql.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return new QueryCache.CachedQuery
                {
                    Sql = cacheKey,
                    Parts = parsedParts,
                    CachedAt = DateTime.UtcNow
                };
            });
            // Always parse the bound SQL since it contains the actual values
            parts = sql.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }
        else
        {
            parts = sql.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }
        
        this.ExecuteInternal(sql, parts, wal);
    }

    /// <summary>
    /// Executes a prepared statement with the cached query plan.
    /// </summary>
    /// <param name="plan">The cached query plan.</param>
    /// <param name="parameters">The parameters to bind.</param>
    /// <param name="wal">The WAL instance.</param>
    public void Execute(CachedQueryPlan plan, Dictionary<string, object?> parameters, IWAL? wal = null)
    {
        var sql = plan.Sql;
        if (parameters != null && parameters.Count > 0)
        {
            sql = SqlParser.BindParameters(sql, parameters);
        }
        else
        {
            sql = SqlParser.SanitizeSql(sql);
        }

        this.ExecuteInternal(sql, plan.Parts, wal);
    }

    /// <summary>
    /// Executes a query and returns the results.
    /// </summary>
    /// <param name="sql">The SQL query.</param>
    /// <param name="parameters">The parameters.</param>
    /// <returns>The query results.</returns>
    public List<Dictionary<string, object>> ExecuteQuery(string sql, Dictionary<string, object?>? parameters = null)
    {
        if (parameters != null && parameters.Count > 0)
        {
            sql = SqlParser.BindParameters(sql, parameters);
        }
        else
        {
            sql = SqlParser.SanitizeSql(sql);
        }

        var parts = sql.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return this.ExecuteQueryInternal(sql, parts);
    }

    /// <summary>
    /// Executes a query and returns the results with optional encryption bypass.
    /// </summary>
    /// <param name="sql">The SQL query.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="noEncrypt">If true, bypasses encryption for this query.</param>
    /// <returns>The query results.</returns>
    public List<Dictionary<string, object>> ExecuteQuery(string sql, Dictionary<string, object?> parameters, bool noEncrypt)
    {
        if (parameters != null && parameters.Count > 0)
        {
            sql = SqlParser.BindParameters(sql, parameters);
        }
        else
        {
            sql = SqlParser.SanitizeSql(sql);
        }

        var parts = sql.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return this.ExecuteQueryInternal(sql, parts, noEncrypt);
    }

    /// <summary>
    /// Executes a query using a cached plan and returns the results.
    /// Skips tokenization/parsing on hot path.
    /// </summary>
    /// <param name="plan">The cached query plan.</param>
    /// <param name="parameters">Optional parameters to bind.</param>
    /// <returns>The query results.</returns>
    public List<Dictionary<string, object>> ExecuteQuery(CachedQueryPlan plan, Dictionary<string, object?>? parameters = null)
    {
        var sql = plan.Sql;
        if (parameters != null && parameters.Count > 0)
        {
            sql = SqlParser.BindParameters(sql, parameters);
        }
        else
        {
            sql = SqlParser.SanitizeSql(sql);
        }

        // Recompute parts to reflect bound SQL (prevents mismatches in WHERE evaluation)
        var parts = sql.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return this.ExecuteQueryInternal(sql, parts);
    }

    /// <summary>
    /// Parses SQL using the enhanced parser with full dialect support and error recovery.
    /// </summary>
    /// <param name="sql">The SQL statement to parse.</param>
    /// <param name="dialect">The SQL dialect to use (defaults to SharpCoreDB).</param>
    /// <returns>The parsed AST node, or null if parsing failed.</returns>
    public static SqlNode? ParseWithEnhancedParser(string sql, ISqlDialect? dialect = null)
    {
        var parser = new EnhancedSqlParser(dialect ?? SqlDialectFactory.Default);
        var ast = parser.Parse(sql);

        if (parser.HasErrors)
        {
            Console.WriteLine("⚠️  SQL Parser Warnings:");
            foreach (var error in parser.Errors)
            {
                Console.WriteLine($"  - {error}");
            }
        }

        return ast;
    }

    /// <summary>
    /// Converts an AST node back to SQL string for debugging.
    /// </summary>
    /// <param name="node">The AST node to convert.</param>
    /// <param name="dialect">The SQL dialect to use.</param>
    /// <returns>The SQL string representation.</returns>
    public static string? AstToSql(SqlNode node, ISqlDialect? dialect = null)
    {
        var visitor = new SqlToStringVisitor(dialect ?? SqlDialectFactory.Default);
        return node.Accept(visitor)?.ToString();
    }

    /// <summary>
    /// Validates SQL syntax without executing it.
    /// </summary>
    /// <param name="sql">The SQL to validate.</param>
    /// <param name="errors">Output list of validation errors.</param>
    /// <returns>True if SQL is valid, false otherwise.</returns>
    public static bool ValidateSql(string sql, out List<string> errors)
    {
        var parser = new EnhancedSqlParser();
        var ast = parser.Parse(sql);
        errors = [.. parser.Errors];
        return ast != null && !parser.HasErrors;
    }
}
