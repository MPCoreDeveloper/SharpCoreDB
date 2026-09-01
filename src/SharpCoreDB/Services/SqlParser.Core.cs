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

    /// <summary>
    /// The owning Database instance. Set by Database after parser creation so that
    /// DDL-created tables can be wired with the database reference for last_insert_rowid tracking.
    /// </summary>
    internal Database? Database { get; set; }

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
        : this(tables, dbPath, (IStorage)null, isReadOnly, queryCache, config)
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
        : this(tables, dbPath, (IStorage)null, isReadOnly, queryCache, config)
    {
        // No factory: DDL is not available without a storage provider.
        _tableFactory = null;
    }

    /// <summary>
    /// Number of rows changed by the last DML statement (for CHANGES() function).
    /// </summary>
    private int _lastChanges;

    /// <summary>
    /// Gets the number of rows affected by the most recently executed DML statement
    /// (INSERT/UPDATE/DELETE). Returns 0 for DDL and for statements that affected no rows.
    /// </summary>
    public int LastChanges => _lastChanges;

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

        string[] parts;
        if (this.queryCache != null)
        {
            // PERF: reuse the cache's tokenized Parts on the hot path. For non-parameterized
            // queries the cache key IS the SQL text, so the cached Parts are exactly the
            // tokens of `sql` — this avoids a per-call Trim().Split() (string[] + one string
            // per token). Parameterized queries embed distinct values into the text per call,
            // so the cached entry (keyed by the unbound SQL) may hold a different binding's
            // tokens — in that case the bound text is tokenized here (as before).
            var entry = this.queryCache.GetOrAdd(originalSql ?? sql, key =>
                new QueryCache.CachedQuery
                {
                    Sql = key,
                    Parts = sql.Trim().Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries),
                    CachedAt = DateTime.UtcNow
                });

            parts = originalSql is null
                ? entry.Parts
                : sql.Trim().Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        }
        else
        {
            parts = sql.Trim().Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
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
        // REMOVED: SanitizeSql was breaking string literals by doubling ALL quotes including delimiters
        // For queries without parameters, we trust the input SQL as-is
        // SQL injection protection should be handled at the application layer via parameterized queries

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
        bool hasParams = parameters is { Count: > 0 };
        if (hasParams)
        {
            sql = SqlParser.BindParameters(sql, parameters);
        }
        // ✅ CRITICAL FIX: Split on ALL whitespace (space, tab, newline, CR, etc.) to handle multi-line SQL correctly.
        // PERF: reuse the cache's tokenized Parts for non-parameterized queries (the cache key
        // is the SQL text itself); parameterized queries re-tokenize the bound text per call.
        string[] parts = this.queryCache is not null && !hasParams
            ? this.queryCache.GetOrAdd(sql, key =>
                new QueryCache.CachedQuery
                {
                    Sql = key,
                    Parts = sql.Trim().Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries),
                    CachedAt = DateTime.UtcNow
                }).Parts
            : sql.Trim().Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
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
        bool hasParams = parameters is { Count: > 0 };
        if (hasParams)
        {
            sql = SqlParser.BindParameters(sql, parameters);
        }
        // ✅ CRITICAL FIX: Split on ALL whitespace (space, tab, newline, CR, etc.) to handle multi-line SQL correctly.
        // EF Core and other callers may pass SQL with newlines, which must be tokenized properly.
        // PERF: reuse the cache's tokenized Parts for non-parameterized queries (see ExecuteQuery above).
        string[] parts = this.queryCache is not null && !hasParams
            ? this.queryCache.GetOrAdd(sql, key =>
                new QueryCache.CachedQuery
                {
                    Sql = key,
                    Parts = sql.Trim().Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries),
                    CachedAt = DateTime.UtcNow
                }).Parts
            : sql.Trim().Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
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
        // v2 fast path: pre-parsed simple point-lookup plans skip parameter binding,
        // tokenization, and SQL re-parsing entirely on repeat executions.
        if (plan.SimpleSelect is null)
        {
            var simple = SimpleSelectPlan.TryCreate(plan.Parts);
            if (simple is not null)
                plan.SimpleSelect = simple;
        }

        if (plan.SimpleSelect is not null &&
            TryExecuteSimpleSelect(plan.SimpleSelect, parameters, out var fastResults))
        {
            return fastResults;
        }

        string sql;
        string[] parts;

        if (parameters != null && parameters.Count > 0)
        {
            // Parameters change the SQL text — must recompute parts
            sql = SqlParser.BindParameters(plan.Sql, parameters);
            parts = sql.Trim().Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        }
        else
        {
            // No parameters — reuse cached SQL and parts as-is
            // REMOVED: SanitizeSql was breaking string literals by doubling ALL quotes including delimiters
            sql = plan.Sql;
            parts = plan.Parts;
        }

        return this.ExecuteQueryInternal(sql, parts);
    }

    /// <summary>
    /// Executes a pre-parsed simple point-lookup plan directly against the table.
    /// Returns false (falling back to the full parser) for any condition that cannot be
    /// handled with exact parity to the legacy string-based path.
    /// </summary>
    private bool TryExecuteSimpleSelect(
        SimpleSelectPlan simple,
        Dictionary<string, object?>? parameters,
        out List<Dictionary<string, object>> results)
    {
        results = [];

        if (!this.tables.TryGetValue(simple.TableName, out var table))
            return false;

        if (simple.WhereColumn is not null)
        {
            // B8: direct hash-index point lookup for `WHERE indexed_col = @param|literal`. This
            // skips building a WHERE string and re-parsing it inside SelectInternal — the single
            // biggest overhead difference vs the Direct API (FindByIndex) on point reads.
            if (TryResolveWhereValue(simple, parameters, out var whereValue) && whereValue is not null)
            {
                if (table is DataStructures.Table concrete &&
                    concrete.TrySelectIndexedPointLookup(simple.WhereColumn, whereValue, out var indexRows))
                {
                    if (simple.Offset.HasValue && simple.Offset.Value > 0)
                        indexRows = [.. indexRows.Skip(simple.Offset.Value)];

                    if (simple.Limit.HasValue && simple.Limit.Value > 0)
                        indexRows = [.. indexRows.Take(simple.Limit.Value)];

                    results = concrete.DeduplicateByPrimaryKey(indexRows);
                    return true;
                }
            }

            // Fallback: build the WHERE string exactly like the legacy binder and let the table
            // scan/index machinery resolve it (non-indexed columns, non-binary collations, …).
            if (!TryBuildSimpleWhereStr(simple, parameters, out var whereStr))
                return false;

            var rows = table.Select(whereStr, simple.OrderByColumn, simple.OrderByAscending, noEncrypt: false);

            // Apply LIMIT/OFFSET exactly like the legacy ExecuteSelectQuery path.
            if (simple.Offset.HasValue && simple.Offset.Value > 0)
                rows = [.. rows.Skip(simple.Offset.Value)];

            if (simple.Limit.HasValue && simple.Limit.Value > 0)
                rows = [.. rows.Take(simple.Limit.Value)];

            results = table is DataStructures.Table concreteTable ? concreteTable.DeduplicateByPrimaryKey(rows) : rows;
            return true;
        }

        // No WHERE (full scan) — the legacy parser handles this shape.
        return false;
    }

    /// <summary>
    /// B8: resolves the simple-SELECT WHERE value as an object (parameter value or literal)
    /// for the direct hash-index point lookup.
    /// </summary>
    private static bool TryResolveWhereValue(
        SimpleSelectPlan simple,
        Dictionary<string, object?>? parameters,
        out object? value)
    {
        value = null;

        if (simple.WhereParameter is not null)
        {
            if (parameters is null || parameters.Count == 0)
                return false;

            if (!TryResolveParameterValue(parameters, simple.WhereParameter, out value))
                return false;

            return value is not null && value != DBNull.Value;
        }

        if (simple.WhereLiteral is not null)
        {
            var literal = simple.WhereLiteral;
            if (literal.Length >= 2 &&
                ((literal[0] == '\'' && literal[^1] == '\'') ||
                 (literal[0] == '"' && literal[^1] == '"')))
            {
                literal = literal[1..^1];
            }

            value = literal;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Resolves a parameter token ("@name", "name", or ":name") against the supplied dictionary.
    /// </summary>
    private static bool TryResolveParameterValue(
        Dictionary<string, object?> parameters,
        string token,
        out object? value)
    {
        if (parameters.TryGetValue(token, out value))
            return true;

        var stripped = token.TrimStart('@', ':');
        if (parameters.TryGetValue(stripped, out value))
            return true;

        if (parameters.TryGetValue("@" + stripped, out value))
            return true;

        if (parameters.TryGetValue(":" + stripped, out value))
            return true;

        value = null;
        return false;
    }

    /// <summary>
    /// Builds the "column = value" WHERE string for a simple point-lookup plan, using the
    /// exact same parameter formatting as the legacy binder so the parser sees identical text.
    /// </summary>
    private bool TryBuildSimpleWhereStr(
        SimpleSelectPlan simple,
        Dictionary<string, object?>? parameters,
        out string whereStr)
    {
        whereStr = string.Empty;

        // The Dictionary-based fast path is a point lookup — a full scan without WHERE
        // must fall back to the legacy parser.
        if (simple.WhereColumn is null)
            return false;

        if (simple.WhereParameter is not null)
        {
            if (parameters is null || parameters.Count == 0)
                return false;

            if (!TryResolveParameterValue(parameters, simple.WhereParameter, out var value) ||
                value is null || value == DBNull.Value)
            {
                return false;
            }

            whereStr = simple.WhereColumn + " = " + ParameterBinder.FormatParameter(value);
            return true;
        }

        if (simple.WhereLiteral is null)
            return false;

        whereStr = simple.WhereColumn + " = " + simple.WhereLiteral;
        return true;
    }

    /// <summary>
    /// Executes a simple point-lookup SELECT and returns zero-allocation <see cref="StructRow"/>
    /// results (struct enumerable — foreach on the returned value is allocation-free). Only the
    /// simple "SELECT [*|col] FROM t [WHERE col = @param|'literal'] [LIMIT n] [OFFSET m]" shape is
    /// supported; any other query shape throws <see cref="NotSupportedException"/>.
    /// </summary>
    public DataStructures.StructRowQueryEnumerable ExecuteQueryStruct(
        CachedQueryPlan plan,
        Dictionary<string, object?>? parameters = null)
    {
        if (plan.SimpleSelect is null)
        {
            var simple = SimpleSelectPlan.TryCreate(plan.Parts);
            if (simple is not null)
                plan.SimpleSelect = simple;
        }

        if (plan.SimpleSelect is not null)
        {
            if (!this.tables.TryGetValue(plan.SimpleSelect.TableName, out var table) || table is not Table concrete)
            {
                return new DataStructures.StructRowQueryEnumerable(null, null, false, 0, null);
            }

            if (plan.SimpleSelect.WhereColumn is null)
            {
                // Full scan (no WHERE) is supported by the zero-alloc StructRow path.
                return new DataStructures.StructRowQueryEnumerable(
                    concrete, null, true, plan.SimpleSelect.Offset ?? 0, plan.SimpleSelect.Limit);
            }

            if (!TryBuildSimpleWhereStr(plan.SimpleSelect, parameters, out var built))
            {
                return new DataStructures.StructRowQueryEnumerable(null, null, false, 0, null);
            }

            return new DataStructures.StructRowQueryEnumerable(
                concrete, built, true, plan.SimpleSelect.Offset ?? 0, plan.SimpleSelect.Limit);
        }

        throw new NotSupportedException(
            "ExecuteQueryStruct supports simple point-lookup SELECTs only. Use ExecuteQuery for full SQL support.");
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
