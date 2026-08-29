using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace SharpCoreDB.EntityFrameworkCore.Storage;

/// <summary>
/// Represents a SharpCoreDB command for Entity Framework Core.
/// Modern C# 14 implementation with full query result support.
/// </summary>
public class SharpCoreDBCommand : DbCommand
{
    private readonly SharpCoreDBConnection _connection;
    private string _commandText = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="SharpCoreDBCommand"/> class.
    /// </summary>
    public SharpCoreDBCommand(SharpCoreDBConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection); // ? C# 14
        _connection = connection;
    }

    /// <inheritdoc />
    [AllowNull]
    public override string CommandText
    {
        get => _commandText;
        set => _commandText = value ?? string.Empty;
    }

    /// <inheritdoc />
    public override int CommandTimeout { get; set; } = 30;

    /// <inheritdoc />
    public override CommandType CommandType { get; set; } = CommandType.Text;

    /// <inheritdoc />
    public override bool DesignTimeVisible { get; set; }

    /// <inheritdoc />
    public override UpdateRowSource UpdatedRowSource { get; set; }

    /// <inheritdoc />
    protected override DbConnection DbConnection
    {
        get => _connection;
        set => throw new NotSupportedException("Cannot change connection for SharpCoreDBCommand.");
    }

    /// <inheritdoc />
    protected override DbParameterCollection DbParameterCollection { get; } = new SharpCoreDBParameterCollection();

    /// <inheritdoc />
    protected override DbTransaction? DbTransaction { get; set; }

    /// <inheritdoc />
    public override void Cancel()
    {
        // Not supported
    }

    /// <inheritdoc />
    public override int ExecuteNonQuery()
    {
        if (_connection.State != ConnectionState.Open)
            throw new InvalidOperationException("Connection must be open.");

        if (_connection.DbInstance is null)
            throw new InvalidOperationException("Database instance is not initialized.");

        var parameters = BuildParameterDictionary();

        var rewritten = RewriteAliasQualifiedSql(_commandText);

        _connection.DbInstance.ExecuteSQL(rewritten, parameters);

        // ✅ Only flush when NOT inside an explicit transaction.
        // Inside a transaction, defer flush so Rollback() can cancel unflushed writes.
        // After commit, the transaction's own Commit() calls Flush().
        var commandUpper = rewritten.Trim().ToUpperInvariant();
        if (DbTransaction is null &&
            (commandUpper.StartsWith("INSERT") ||
             commandUpper.StartsWith("UPDATE") ||
             commandUpper.StartsWith("DELETE")))
        {
            _connection.DbInstance.Flush();
        }

        // ✅ Issue #340: return the real affected-row count (SQLite changes() parity).
        // Previously this hardcoded 1, breaking DELETE/UPDATE affected counts.
        return _connection.DbInstance.GetLastChanges();
    }

    /// <inheritdoc />
    public override object? ExecuteScalar()
    {
        using var reader = ExecuteReader();
        if (reader.Read() && reader.FieldCount > 0)
        {
            return reader.GetValue(0);
        }
        return null;
    }

    /// <inheritdoc />
    public override void Prepare()
    {
        // Not supported for SharpCoreDB
    }

    /// <inheritdoc />
    protected override DbParameter CreateDbParameter() => new SharpCoreDBParameter(); // ? C# 14: expression-bodied

    /// <inheritdoc />
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        if (_connection.State != ConnectionState.Open)
            throw new InvalidOperationException("Connection must be open.");

        if (_connection.DbInstance is null)
            throw new InvalidOperationException("Database instance is not initialized.");

        var parameters = BuildParameterDictionary();

        // ✅ FIX: Rewrite alias-qualified column refs that SharpCoreDB parser cannot handle.
        // EF Core generates SELECT "b"."BlogId", "b"."Title" FROM "Blogs" AS "b".
        // The SharpCoreDB parser returns dictionary keys like 'b"."BlogId' verbatim.
        // Strip the alias prefix from every column reference in the SELECT list.
        var rewritten = RewriteAliasQualifiedSql(_commandText);

        // ✅ FIX: Handle multi-statement SQL produced by AppendInsertOperation.
        // INSERT ...; SELECT last_insert_rowid();
        // Run DML via ExecuteSQL, then run the final SELECT via ExecuteQuery.
        var statements = SplitStatements(rewritten);
        if (statements.Count > 1)
        {
            for (var i = 0; i < statements.Count - 1; i++)
            {
                var dml = statements[i].Trim();
                if (!string.IsNullOrWhiteSpace(dml))
                {
                    _connection.DbInstance.ExecuteSQL(dml, parameters);
                    var upper = dml.ToUpperInvariant();
                    if (DbTransaction is null &&
                        (upper.StartsWith("INSERT") || upper.StartsWith("UPDATE") || upper.StartsWith("DELETE")))
                    {
                        _connection.DbInstance.Flush();
                    }
                }
            }

            var selectSql = statements[^1].Trim();
            if (!string.IsNullOrWhiteSpace(selectSql))
            {
                // For the specific case of retrieving the generated key, use the
                // database's direct API instead of relying on last_insert_rowid() SQL
                // function, which may have visibility issues after Flush().
                if (selectSql.Contains("last_insert_rowid", StringComparison.OrdinalIgnoreCase))
                {
                    var rowId = _connection.DbInstance.GetLastInsertRowId();
                    var synthetic = new List<Dictionary<string, object>>
                    {
                        new Dictionary<string, object> { ["BlogId"] = (int)rowId }
                    };
                    return new SharpCoreDBDataReader(synthetic);
                }

                var results = _connection.DbInstance.ExecuteQuery(selectSql, parameters);
                return new SharpCoreDBDataReader(results);
            }

            return new SharpCoreDBDataReader();
        }

        // Single statement path
        var upper0 = rewritten.TrimStart().ToUpperInvariant();
        if (upper0.StartsWith("INSERT") || upper0.StartsWith("UPDATE") || upper0.StartsWith("DELETE"))
        {
            // DML without a trailing SELECT – run via ExecuteSQL (not ExecuteQuery)
            _connection.DbInstance.ExecuteSQL(rewritten, parameters);
            if (DbTransaction is null)
                _connection.DbInstance.Flush();
            return new SharpCoreDBDataReader();
        }

        // Single statement – execute as query directly (SELECT path)
        var queryResults = _connection.DbInstance.ExecuteQuery(rewritten, parameters);

        // ✅ ROBUST EF PROVIDER: Always project when we have a clean column list.
        // Fallback to original results if projection fails (backwards safety).
        // Skip projection for aggregate queries (COUNT, SUM, etc.) – the engine
        // returns a renamed column (e.g. "cnt") and projection would produce empty rows.
        //
        // IMPORTANT: For split-include child queries (common with Guid PK/FK navigation materialization),
        // we are more conservative. EF Core's shaper often relies on extra correlation columns
        // (e.g. the parent's Id) that may not be in the top-level requested list after alias stripping.
        // In those cases we prefer the full engine results to avoid ordinal mismatches in the DataReader.
        var requested = ExtractRequestedColumns(rewritten);
        bool looksLikeRelationshipMaterializationQuery = IsLikelyRelationshipMaterializationQuery(rewritten, requested);

        if (requested.Count > 0 &&
            !requested.Any(c => c == "*" || c.Equals("ALL", StringComparison.OrdinalIgnoreCase)) &&
            !requested.Any(c => IsAggregateExpression(c)) &&
            !looksLikeRelationshipMaterializationQuery)
        {
            try
            {
                queryResults = ProjectColumns(queryResults, requested);
            }
            catch
            {
                // keep original results for compatibility
            }
        }

        return new SharpCoreDBDataReader(queryResults);
    }

    /// <summary>
    /// Rewrites EF Core generated SQL into a form the SharpCoreDB legacy parser understands.
    /// We keep aliases and qualification intact (critical for joins, composite keys, and
    /// correlated subqueries) and only remove double quotes around identifiers.
    /// </summary>
    private static string RewriteAliasQualifiedSql(string sql)
    {
        // Remove identifier quotes outside string literals while preserving alias-qualified
        // expressions like c.Id = v.CompanyId.
        return ReplaceOutsideStringLiterals(sql, IdentifierDoubleQuotePattern, static _ => string.Empty);
    }

    /// <summary>
    /// Applies a regex replacement only to portions of SQL that are outside single-quoted string literals.
    /// This prevents URL-like patterns (e.g. 'https://myblog.com') from being incorrectly rewritten.
    /// </summary>
    private static string ReplaceOutsideStringLiterals(string sql, Regex pattern, MatchEvaluator evaluator)
    {
        var result = new System.Text.StringBuilder(sql.Length);
        var i = 0;
        while (i < sql.Length)
        {
            if (sql[i] == '\'')
            {
                // Copy entire single-quoted literal verbatim
                result.Append(sql[i++]);
                while (i < sql.Length)
                {
                    // Handle escaped quotes first
                    if (sql[i] == '\'' && i + 1 < sql.Length && sql[i + 1] == '\'')
                    {
                        // Escaped quote '' – append both and skip ahead
                        result.Append(sql[i]);
                        result.Append(sql[i + 1]);
                        i += 2;
                    }
                    else if (sql[i] == '\'')
                    {
                        // Closing quote – append it and break
                        result.Append(sql[i]);
                        i++;
                        break;
                    }
                    else
                    {
                        // Regular character inside the string
                        result.Append(sql[i]);
                        i++;
                    }
                }
            }
            else
            {
                // Find the next single quote (or end of string) and apply pattern to this segment
                var nextQuote = sql.IndexOf('\'', i);
                var segment = nextQuote < 0 ? sql[i..] : sql[i..nextQuote];
                result.Append(pattern.Replace(segment, evaluator));
                i = nextQuote < 0 ? sql.Length : nextQuote;
            }
        }
        return result.ToString();
    }

    // Matches a single double-quote for identifier de-quoting.
    private static readonly Regex IdentifierDoubleQuotePattern = new("\"", RegexOptions.Compiled, System.TimeSpan.FromSeconds(1));

    private static List<string> SplitStatements(string sql)
    {
        // Split on semicolons that are not inside string literals.
        // This enables "INSERT ...; SELECT last_insert_rowid() AS \"BlogId\"" for key retrieval.
        var statements = new List<string>();
        var current = new System.Text.StringBuilder();
        var inString = false;
        var stringChar = '\0';

        foreach (var ch in sql)
        {
            if (!inString && (ch == '\'' || ch == '"'))
            {
                inString = true;
                stringChar = ch;
                current.Append(ch);
            }
            else if (inString && ch == stringChar)
            {
                inString = false;
                current.Append(ch);
            }
            else if (!inString && ch == ';')
            {
                var stmt = current.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(stmt))
                    statements.Add(stmt);
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        var last = current.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(last))
            statements.Add(last);

        return statements;
    }

    /// <summary>
    /// Extracts requested columns from a rewritten SELECT statement for EF projection.
    /// Handles EF-generated SQL with or without aliases and quoted identifiers.
    /// </summary>
    private static List<string> ExtractRequestedColumns(string sql)
    {
        var upper = sql.ToUpperInvariant();
        var selectIdx = upper.IndexOf("SELECT", StringComparison.Ordinal);
        var fromIdx = upper.IndexOf("FROM", StringComparison.Ordinal);
        if (selectIdx < 0 || fromIdx <= selectIdx) return [];

        var selectPart = sql.Substring(selectIdx + 6, fromIdx - selectIdx - 6).Trim();
        if (string.IsNullOrWhiteSpace(selectPart)) return [];

        var columns = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuote = false;
        char quoteChar = '\0';

        foreach (var ch in selectPart)
        {
            if (!inQuote && (ch == '"' || ch == '\'' || ch == '[' || ch == '`'))
            {
                inQuote = true;
                quoteChar = ch == '[' ? ']' : ch;
                current.Append(ch);
            }
            else if (inQuote && ch == quoteChar)
            {
                inQuote = false;
                current.Append(ch);
            }
            else if (!inQuote && ch == ',')
            {
                var name = current.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(name))
                    columns.Add(name.Trim('"', '[', ']', '`', ' '));
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        var last = current.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(last))
            columns.Add(last.Trim('"', '[', ']', '`', ' '));

        return columns;
    }

    /// <summary>
    /// Projects full result rows to only the requested columns using normalized names.
    /// Only includes columns that exist in source rows; preserves NULL values from source.
    /// </summary>
    private static List<Dictionary<string, object>> ProjectColumns(
        List<Dictionary<string, object>> rows,
        List<string> requestedColumns)
    {
        if (rows.Count == 0 || requestedColumns.Count == 0) return rows;

        var projected = new List<Dictionary<string, object>>(rows.Count);
        foreach (var row in rows)
        {
            var newRow = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var col in requestedColumns)
            {
                var normalized = col.Trim('"', '[', ']', '`', ' ');

                // EF Core frequently requests alias-qualified columns (e.g. b.BlogId)
                // while engine rows expose bare names (e.g. BlogId).
                var dotIdx = normalized.LastIndexOf('.');
                var bare = dotIdx >= 0 && dotIdx < normalized.Length - 1
                    ? normalized[(dotIdx + 1)..].Trim('"', '[', ']', '`', ' ')
                    : normalized;

                // Try exact match first (qualified or bare)
                if (row.TryGetValue(normalized, out var exact))
                {
                    newRow[normalized] = exact; // Preserve actual value (may be DBNull from source)
                    continue;
                }

                var isQualifiedRequest = dotIdx >= 0;

                // For qualified requests (e.g. v.Id), avoid blindly falling back to bare Id
                // when qualified keys exist in the row; otherwise child columns can be
                // populated from parent values in LEFT JOIN no-match rows.
                var hasAnyQualifiedForBare = row.Keys.Any(k =>
                    k.EndsWith($".{bare}", StringComparison.OrdinalIgnoreCase));

                if (!string.Equals(bare, normalized, StringComparison.OrdinalIgnoreCase) &&
                    row.TryGetValue(bare, out var bareExact) &&
                    (!isQualifiedRequest || !hasAnyQualifiedForBare))
                {
                    newRow[normalized] = bareExact;
                    continue;
                }

                // Try case-insensitive or qualified match
                var match = row.FirstOrDefault(kv =>
                    string.Equals(kv.Key, normalized, StringComparison.OrdinalIgnoreCase) ||
                    (!isQualifiedRequest && string.Equals(kv.Key, bare, StringComparison.OrdinalIgnoreCase)) ||
                    kv.Key.EndsWith($".{normalized}", StringComparison.OrdinalIgnoreCase) ||
                    kv.Key.EndsWith($".{bare}", StringComparison.OrdinalIgnoreCase));

                if (!match.Equals(default(KeyValuePair<string, object>)))
                {
                    // For qualified requests, only accept exact qualifier matches.
                    if (isQualifiedRequest)
                    {
                        var qualifier = normalized[..dotIdx];
                        var candidateKey = match.Key;
                        var candidateDot = candidateKey.LastIndexOf('.');
                        if (candidateDot > 0)
                        {
                            var candidateQualifier = candidateKey[..candidateDot];
                            if (!string.Equals(candidateQualifier, qualifier, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                        }
                        else
                        {
                            // Candidate is bare key; accept only if no qualified candidates exist.
                            if (hasAnyQualifiedForBare)
                            {
                                continue;
                            }
                        }
                    }

                    newRow[normalized] = match.Value; // Found it, use actual value
                }
                // else: column not found in source, don't add it (missing column = skip)
            }
            projected.Add(newRow);
        }
        return projected;
    }

    /// <summary>
    /// Returns true if the column expression is an aggregate function call (COUNT, SUM, AVG, MIN, MAX, etc.).
    /// Aggregate queries return engine-renamed columns (e.g. "cnt"), so column projection must be skipped.
    /// </summary>
    private static bool IsAggregateExpression(string col)
    {
        var trimmed = col.Trim();
        return trimmed.StartsWith("COUNT(", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("SUM(", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("AVG(", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("MIN(", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("MAX(", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Detects query shapes that likely belong to EF Core relationship/include materialization.
    /// These query paths can rely on correlation columns and strict ordinals, so column projection
    /// should be skipped to avoid losing columns needed by the shaper.
    /// </summary>
    private static bool IsLikelyRelationshipMaterializationQuery(string sql, List<string> requestedColumns)
    {
        if (requestedColumns.Count == 0)
            return false;

        var loweredSql = sql.ToLowerInvariant();
        var hasJoin = loweredSql.Contains(" join ", StringComparison.Ordinal);
        var hasInSubquery = loweredSql.Contains(" in (select ", StringComparison.Ordinal);
        var hasOrderBy = loweredSql.Contains(" order by ", StringComparison.Ordinal);

        var normalizedColumns = requestedColumns
            .Select(static c => c.Trim().Trim('"', '[', ']', '`').ToLowerInvariant())
            .ToList();

        var hasIdColumn = normalizedColumns.Any(static c => c == "id" || c.EndsWith(".id", StringComparison.Ordinal));
        var hasForeignKeyColumn = normalizedColumns.Any(static c => c.EndsWith("id", StringComparison.Ordinal) && c != "id");

        // Common EF include/split materialization shapes:
        // - query includes key + FK columns
        // - query uses JOIN or IN (SELECT ...) to correlate parent/child rows
        // - query often has deterministic ORDER BY over key columns
        return (hasJoin || hasInSubquery || hasOrderBy) && hasIdColumn && hasForeignKeyColumn;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
    {
        if (_connection.State != ConnectionState.Open)
            throw new InvalidOperationException("Connection must be open.");

        if (_connection.DbInstance is null)
            throw new InvalidOperationException("Database instance is not initialized.");

        var parameters = BuildParameterDictionary();

        // ✅ FIX: Rewrite EF Core SQL before executing (same as ExecuteDbDataReader)
        // EF Core generates: DELETE FROM "Blogs" WHERE "b"."BlogId" = @p0
        // SharpCoreDB needs: DELETE FROM Blogs WHERE BlogId = @p0
        var rewritten = RewriteAliasQualifiedSql(_commandText);

        await _connection.DbInstance.ExecuteSQLAsync(rewritten, parameters, cancellationToken).ConfigureAwait(false);

        // ✅ Only flush when NOT inside an explicit transaction.
        var commandUpper = rewritten.Trim().ToUpperInvariant();
        if (DbTransaction is null &&
            (commandUpper.StartsWith("INSERT") ||
             commandUpper.StartsWith("UPDATE") ||
             commandUpper.StartsWith("DELETE")))
        {
            _connection.DbInstance.Flush();
        }

        // ✅ Issue #340: return the real affected-row count (SQLite changes() parity).
        return _connection.DbInstance.GetLastChanges();
    }

    /// <inheritdoc />
    public override async Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        using var reader = await ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false) && reader.FieldCount > 0)
        {
            return reader.GetValue(0);
        }
        return null;
    }

    /// <inheritdoc />
    /// <remarks>
    /// EF Core calls ExecuteReaderAsync for DML operations (INSERT/UPDATE/DELETE).
    /// This override ensures the synchronous ExecuteDbDataReader is called properly.
    /// </remarks>
    protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
    {
        // Delegate to the synchronous implementation which handles SQL rewriting
        return await Task.Run(() => ExecuteDbDataReader(behavior), cancellationToken).ConfigureAwait(false);
    }

    private Dictionary<string, object?> BuildParameterDictionary()
    {
        var parameters = new Dictionary<string, object?>();
        foreach (SharpCoreDBParameter param in DbParameterCollection)
        {
            var value = param.Value;

            // Normalize temporal + Guid parameters.
            // Guids must be converted to a canonical string because the underlying
            // SharpCoreDB engine + storage layer does not reliably round-trip raw
            // Guid objects when used as foreign keys (unlike integer keys).
            // This is the root cause of the "Guid FKs not visible after Include" bug.
            value = value switch
            {
                DateTime dt => dt.ToUniversalTime().ToString("o"),
                DateTimeOffset dto => dto.UtcDateTime.ToString("o"),
                Guid g => g.ToString("D"),           // Canonical format for reliability
                _ => value
            };

            parameters[param.ParameterName.TrimStart('@', ':')] = value;
        }
        return parameters;
    }
}

/// <summary>
/// Parameter collection for SharpCoreDB commands.
/// Modern C# 14 with collection expressions.
/// </summary>
public class SharpCoreDBParameterCollection : DbParameterCollection
{
    private readonly List<DbParameter> _parameters = []; // ? C# 14: collection expression

    /// <summary>Gets the number of parameters in the collection.</summary>
    public override int Count => _parameters.Count;
    
    /// <summary>Gets the synchronization root.</summary>
    public override object SyncRoot => _parameters;

    /// <summary>Adds a parameter to the collection.</summary>
    /// <param name="value">The parameter to add.</param>
    /// <returns>The index of the added parameter.</returns>
    public override int Add([AllowNull] object value)
    {
        if (value is not DbParameter param) // ? C# 14: not pattern
            throw new ArgumentException("Value must be a DbParameter", nameof(value));
            
        _parameters.Add(param);
        return _parameters.Count - 1;
    }

    /// <summary>Adds a range of parameters to the collection.</summary>
    /// <param name="values">The parameters to add.</param>
    public override void AddRange(Array values) => _parameters.AddRange(values.Cast<DbParameter>());
    
    /// <summary>Clears the collection.</summary>
    public override void Clear() => _parameters.Clear();
    
    /// <summary>Determines whether the collection contains the specified parameter.</summary>
    /// <param name="value">The parameter to check.</param>
    /// <returns>True if the parameter is in the collection, otherwise false.</returns>
    public override bool Contains(object value) => value is DbParameter param && _parameters.Contains(param);
    
    /// <summary>Determines whether the collection contains a parameter with the specified name.</summary>
    /// <param name="value">The parameter name to check.</param>
    /// <returns>True if the parameter is in the collection, otherwise false.</returns>
    public override bool Contains(string value) => _parameters.Any(p => p.ParameterName == value);
    
    /// <summary>Copies the parameters to an array.</summary>
    /// <param name="array">The array to copy to.</param>
    /// <param name="index">The starting index.</param>
    public override void CopyTo(Array array, int index) => _parameters.CopyTo((DbParameter[])array, index);
    
    /// <summary>Gets an enumerator for the collection.</summary>
    /// <returns>An enumerator.</returns>
    public override System.Collections.IEnumerator GetEnumerator() => _parameters.GetEnumerator();
    
    /// <summary>Gets the index of the specified parameter.</summary>
    /// <param name="value">The parameter to find.</param>
    /// <returns>The index of the parameter.</returns>
    public override int IndexOf(object value) => value is DbParameter param ? _parameters.IndexOf(param) : -1;
    
    /// <summary>Gets the index of the parameter with the specified name.</summary>
    /// <param name="parameterName">The parameter name.</param>
    /// <returns>The index of the parameter.</returns>
    public override int IndexOf(string parameterName) => _parameters.FindIndex(p => p.ParameterName == parameterName);
    
    /// <summary>Inserts a parameter at the specified index.</summary>
    /// <param name="index">The index to insert at.</param>
    /// <param name="value">The parameter to insert.</param>
    public override void Insert(int index, [AllowNull] object value)
    {
        if (value is DbParameter param)
            _parameters.Insert(index, param);
    }
    
    /// <summary>Removes the specified parameter.</summary>
    /// <param name="value">The parameter to remove.</param>
    public override void Remove([AllowNull] object value)
    {
        if (value is DbParameter param)
            _parameters.Remove(param);
    }
    
    /// <summary>Removes the parameter at the specified index.</summary>
    /// <param name="index">The index to remove at.</param>
    public override void RemoveAt(int index) => _parameters.RemoveAt(index);
    
    /// <summary>Removes the parameter with the specified name.</summary>
    /// <param name="parameterName">The parameter name.</param>
    public override void RemoveAt(string parameterName)
    {
        var index = IndexOf(parameterName);
        if (index >= 0)
            RemoveAt(index);
    }
    
    /// <summary>Gets the parameter at the specified index.</summary>
    /// <param name="index">The index.</param>
    /// <returns>The parameter.</returns>
    protected override DbParameter GetParameter(int index) => _parameters[index];
    
    /// <summary>Gets the parameter with the specified name.</summary>
    /// <param name="parameterName">The parameter name.</param>
    /// <returns>The parameter.</returns>
    protected override DbParameter GetParameter(string parameterName)
    {
        return _parameters.FirstOrDefault(p => p.ParameterName == parameterName)
               ?? throw new ArgumentException($"Parameter {parameterName} not found", nameof(parameterName));
    }
    
    /// <summary>Sets the parameter at the specified index.</summary>
    /// <param name="index">The index.</param>
    /// <param name="value">The parameter.</param>
    protected override void SetParameter(int index, DbParameter value) => _parameters[index] = value;
    
    /// <summary>Sets the parameter with the specified name.</summary>
    /// <param name="parameterName">The parameter name.</param>
    /// <param name="value">The parameter.</param>
    protected override void SetParameter(string parameterName, DbParameter value)
    {
        var index = IndexOf(parameterName);
        if (index >= 0)
            _parameters[index] = value;
    }
}

/// <summary>
/// Parameter for SharpCoreDB commands.
/// Modern C# 14 with init properties.
/// </summary>
public class SharpCoreDBParameter : DbParameter
{
    /// <summary>Gets or sets the database type.</summary>
    public override DbType DbType { get; set; }
    
    /// <summary>Gets or sets the parameter direction.</summary>
    public override ParameterDirection Direction { get; set; }
    
    /// <summary>Gets or sets whether the parameter is nullable.</summary>
    public override bool IsNullable { get; set; }
    
    /// <summary>Gets or sets the parameter name.</summary>
    public override string ParameterName { get; set; } = string.Empty;
    
    /// <summary>Gets or sets the source column.</summary>
    public override string SourceColumn { get; set; } = string.Empty;
    
    /// <summary>Gets or sets the parameter value.</summary>
    public override object? Value { get; set; }
    
    /// <summary>Gets or sets whether the source column null mapping is used.</summary>
    public override bool SourceColumnNullMapping { get; set; }
    
    /// <summary>Gets or sets the parameter size.</summary>
    public override int Size { get; set; }

    /// <summary>Resets the database type to string.</summary>
    public override void ResetDbType() => DbType = DbType.String;
}
