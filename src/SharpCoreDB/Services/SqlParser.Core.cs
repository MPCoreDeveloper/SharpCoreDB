// <copyright file="SqlParser.Core.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Storage;

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

    // Wraps storage in a factory so DDL can create tables without being hard-coupled to IStorage.
    // Set once in the primary constructor; the DML-only constructor leaves this null (DDL will throw).
    private readonly ITableFactory _tableFactory = new DirectoryTableFactory(storage);

    /// <summary>
    /// Creates a <see cref="SqlParser"/> backed by an explicit <see cref="ITableFactory"/>.
    /// This constructor supports full DDL (CREATE/DROP TABLE) using whatever storage the factory
    /// provides — for example a <c>SingleFileTableFactory</c> for <c>.scdb</c> databases.
    /// </summary>
    /// <param name="tables">The live table dictionary shared with the owning database.</param>
    /// <param name="dbPath">Database path (used for error messages and index file paths).</param>
    /// <param name="tableFactory">Factory used to create new tables during DDL execution.</param>
    /// <param name="isReadOnly">Whether the database is opened read-only.</param>
    /// <param name="queryCache">Optional shared query cache.</param>
    /// <param name="config">Optional database configuration.</param>
    internal SqlParser(Dictionary<string, ITable> tables, string dbPath, ITableFactory tableFactory, bool isReadOnly = false, QueryCache? queryCache = null, DatabaseConfig? config = null)
        : this(tables, dbPath, (IStorage)null!, isReadOnly, queryCache, config)
    {
        // Override the directory-mode factory set by the primary constructor with the provided one.
        _tableFactory = tableFactory ?? throw new ArgumentNullException(nameof(tableFactory));
    }

    /// <summary>
    /// Creates a <see cref="SqlParser"/> that can execute DML and SELECT statements against an
    /// existing table dictionary.  DDL operations (CREATE/DROP TABLE) are not supported via this
    /// constructor because no storage provider is available to persist new table files.
    /// </summary>
    /// <param name="tables">The populated table dictionary (e.g. from <see cref="SingleFileDatabase"/>).</param>
    /// <param name="dbPath">Database path (used for error messages and index file paths).</param>
    /// <param name="isReadOnly">Whether the database is opened read-only.</param>
    /// <param name="queryCache">Optional shared query cache.</param>
    /// <param name="config">Optional database configuration.</param>
    internal SqlParser(Dictionary<string, ITable> tables, string dbPath, bool isReadOnly = false, QueryCache? queryCache = null, DatabaseConfig? config = null)
        : this(tables, dbPath, (IStorage)null!, isReadOnly, queryCache, config)
    {
        // No factory: DDL is not available without a storage provider.
        _tableFactory = null!;
    }

    /// <summary>
    /// Number of rows changed by the last DML statement (for CHANGES() function).
    /// </summary>
    private int _lastChanges;

    /// <summary>
    /// Cumulative number of rows changed since the connection was opened (for TOTAL_CHANGES() function).
    /// </summary>
    private int _totalChanges;

    /// <summary>
    /// Row ID of the last inserted row (for LAST_INSERT_ROWID() function).
    /// </summary>
    private long _lastInsertRowId;

    /// <summary>
    /// Temporary query buffer for statements that return rows from non-SELECT operations (e.g., DML RETURNING).
    /// </summary>
    private List<Dictionary<string, object>> _pendingQueryResults = [];

    /// <summary>
    /// Optional vector query optimizer registered by the VectorSearch module.
    /// When set, the query planner detects ORDER BY vec_distance_*() + LIMIT patterns
    /// and routes them to a vector index instead of a full table scan.
    /// Thread-safe: set once during Database initialization, read from any thread.
    /// </summary>
    internal static IVectorQueryOptimizer? VectorQueryOptimizer { get; set; }

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
        string sql;
        string[] parts;

        if (parameters != null && parameters.Count > 0)
        {
            // Parameters change the SQL text — must recompute parts
            sql = SqlParser.BindParameters(plan.Sql, parameters);
            parts = sql.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }
        else
        {
            // No parameters — reuse cached parts (skip re-tokenization)
            sql = SqlParser.SanitizeSql(plan.Sql);
            parts = plan.Parts;
        }

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
