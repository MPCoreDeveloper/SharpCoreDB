// <copyright file="SqlParser.InformationSchema.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// SqlParser partial class implementing the SQL-standard <c>information_schema</c> metadata views
/// from the live schema: <c>tables</c>, <c>views</c>, <c>columns</c>, <c>schemata</c>,
/// <c>triggers</c> and the SharpCoreDB-specific <c>indexes</c>.
/// <para>
/// This lives in the <b>engine</b>, not in a protocol handler, so every caller sees the same rows:
/// embedded <c>Database.ExecuteQuery</c>, the REST endpoint, the WebSocket endpoint and the
/// PostgreSQL binary protocol. The server layer (<c>PgCatalogService</c>) still intercepts first on
/// the PostgreSQL protocol; this implementation is the answer for every other entry point.
/// </para>
/// <para>
/// SQLite has no <c>information_schema</c>, so this is additive capability: <c>sqlite_master</c>,
/// <c>PRAGMA</c> and every existing path keep working unchanged. Behaviour notes:
/// <list type="bullet">
/// <item>supported shapes — explicit column lists with <c>AS</c> aliases, <c>*</c>,
/// <c>WHERE</c> (<c>=</c>, <c>&lt;&gt;</c>, <c>!=</c>, <c>&lt;</c>, <c>&lt;=</c>, <c>&gt;</c>, <c>&gt;=</c>,
/// <c>LIKE</c>, <c>NOT LIKE</c>, <c>IN</c>, <c>NOT IN</c>, <c>IS [NOT] NULL</c>, <c>AND</c>/<c>OR</c>,
/// parentheses), <c>ORDER BY</c> (multi-column, <c>ASC</c>/<c>DESC</c>), <c>LIMIT</c>/<c>OFFSET</c>,
/// and <c>COUNT(*)</c> — which returns the engine's usual <c>cnt</c> column, matching the generic
/// aggregate path. A predicate on a column the view does not expose, or any other expression,
/// throws instead of silently answering the wrong question.</item>
/// <item><c>information_schema.tables</c> lists base tables <b>and</b> views (as PostgreSQL does),
/// views with <c>table_type = 'VIEW'</c>.</item>
/// <item><c>information_schema.columns</c> covers base tables only: a view's columns are the columns
/// of an arbitrary SELECT, so they cannot be reported without executing the definition.</item>
/// <item>the constraint/routine views (<c>table_constraints</c>, <c>key_column_usage</c>,
/// <c>referential_constraints</c>, <c>constraint_column_usage</c>, <c>check_constraints</c>,
/// <c>routines</c>, <c>parameters</c>) are recognised and return an empty result set with their
/// standard column list — the engine keeps no catalog for them. Any other
/// <c>information_schema.&lt;name&gt;</c> fails with <c>information_schema.&lt;name&gt; does not exist</c>.</item>
/// </list>
/// </para>
/// </summary>
public partial class SqlParser
{
    /// <summary>Schema name that selects this metadata layer.</summary>
    internal const string InformationSchemaSchemaName = "information_schema";

    /// <summary>Catalog name used when the database path carries no usable name.</summary>
    internal const string InformationSchemaFallbackCatalog = "main";

    /// <summary>Schema reported for engine tables — kept identical to the server catalog layer.</summary>
    internal const string InformationSchemaDefaultSchema = "public";

    /// <summary>
    /// Locates an <c>information_schema.&lt;view&gt;</c> source in the FROM clause. Matching the FROM
    /// clause (rather than the mere presence of the text) keeps a real table that is filtered on the
    /// literal <c>'information_schema'</c> on the normal path.
    /// </summary>
    private static readonly Regex InformationSchemaSourceRegex = new(
        @"\bFROM\s+[""`\[]?information_schema[""`\]]?\s*\.\s*[""`\[]?(?<view>[A-Za-z_][A-Za-z0-9_]*)[""`\]]?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    /// <summary>Columns of <c>information_schema.tables</c> (PostgreSQL-compatible).</summary>
    private static readonly string[] InformationSchemaTablesColumns =
    [
        "table_catalog", "table_schema", "table_name", "table_type",
        "self_referencing_column_name", "reference_generation",
        "user_defined_type_catalog", "user_defined_type_schema", "user_defined_type_name",
        "is_insertable_into", "is_typed", "commit_action"
    ];

    /// <summary>Columns of <c>information_schema.schemata</c> (PostgreSQL-compatible).</summary>
    private static readonly string[] InformationSchemaSchemataColumns =
    [
        "catalog_name", "schema_name", "schema_owner",
        "default_character_set_catalog", "default_character_set_schema",
        "default_character_set_name", "sql_path"
    ];

    /// <summary>Columns of <c>information_schema.views</c> (PostgreSQL-compatible).</summary>
    private static readonly string[] InformationSchemaViewsColumns =
    [
        "table_catalog", "table_schema", "table_name", "view_definition", "check_option",
        "is_updatable", "is_insertable_into", "is_trigger_updatable",
        "is_trigger_deletable", "is_trigger_insertable_into"
    ];

    /// <summary>Columns of <c>information_schema.columns</c> (PostgreSQL-compatible).</summary>
    private static readonly string[] InformationSchemaColumnsColumns =
    [
        "table_catalog", "table_schema", "table_name", "column_name",
        "ordinal_position", "column_default", "is_nullable", "data_type",
        "character_maximum_length", "character_octet_length",
        "numeric_precision", "numeric_precision_radix", "numeric_scale",
        "datetime_precision", "interval_type", "interval_precision",
        "character_set_catalog", "character_set_schema", "character_set_name",
        "collation_catalog", "collation_schema", "collation_name",
        "domain_catalog", "domain_schema", "domain_name",
        "udt_catalog", "udt_schema", "udt_name",
        "scope_catalog", "scope_schema", "scope_name",
        "maximum_cardinality", "dtd_identifier", "is_self_referencing",
        "is_identity", "identity_generation", "identity_start", "identity_increment",
        "identity_maximum", "identity_minimum", "identity_cycle",
        "is_generated", "generation_expression", "is_updatable"
    ];

    /// <summary>Columns of <c>information_schema.triggers</c> (PostgreSQL-compatible).</summary>
    private static readonly string[] InformationSchemaTriggersColumns =
    [
        "trigger_catalog", "trigger_schema", "trigger_name", "event_manipulation",
        "event_object_catalog", "event_object_schema", "event_object_table",
        "action_order", "action_condition", "action_statement", "action_orientation",
        "action_timing", "action_reference_old_table", "action_reference_new_table",
        "action_reference_old_row", "action_reference_new_row", "created"
    ];

    /// <summary>
    /// Columns of <c>information_schema.indexes</c>. This view is SharpCoreDB-specific — PostgreSQL
    /// exposes indexes through <c>pg_indexes</c> instead — and is the one the vector documentation
    /// points at for index discovery (<c>docs/Vectors/VECTOR_MIGRATION_GUIDE.md</c>).
    /// </summary>
    private static readonly string[] InformationSchemaIndexesColumns =
    [
        "table_catalog", "table_schema", "table_name", "index_name",
        "column_name", "index_type", "is_unique", "is_primary"
    ];



    /// <summary>Recognised metadata views the engine keeps no catalog for; they answer empty.</summary>
    private static readonly Dictionary<string, string[]> InformationSchemaEmptyViews =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["table_constraints"] =
            [
                "constraint_catalog", "constraint_schema", "constraint_name",
                "table_catalog", "table_schema", "table_name", "constraint_type",
                "is_deferrable", "initially_deferred", "enforced"
            ],
            ["key_column_usage"] =
            [
                "constraint_catalog", "constraint_schema", "constraint_name",
                "table_catalog", "table_schema", "table_name", "column_name",
                "ordinal_position", "position_in_unique_constraint"
            ],
            ["referential_constraints"] =
            [
                "constraint_catalog", "constraint_schema", "constraint_name",
                "unique_constraint_catalog", "unique_constraint_schema", "unique_constraint_name",
                "match_option", "update_rule", "delete_rule"
            ],
            ["constraint_column_usage"] =
            [
                "table_catalog", "table_schema", "table_name", "column_name",
                "constraint_catalog", "constraint_schema", "constraint_name"
            ],
            ["check_constraints"] =
            [
                "constraint_catalog", "constraint_schema", "constraint_name", "check_clause"
            ],
            ["routines"] =
            [
                "specific_catalog", "specific_schema", "specific_name",
                "routine_catalog", "routine_schema", "routine_name", "routine_type",
                "module_catalog", "module_schema", "module_name",
                "udt_catalog", "udt_schema", "udt_name", "data_type",
                "character_maximum_length", "character_octet_length",
                "character_set_catalog", "character_set_schema", "character_set_name",
                "collation_catalog", "collation_schema", "collation_name",
                "numeric_precision", "numeric_precision_radix", "numeric_scale",
                "datetime_precision", "interval_type", "interval_precision",
                "type_udt_catalog", "type_udt_schema", "type_udt_name",
                "scope_catalog", "scope_schema", "scope_name", "maximum_cardinality",
                "dtd_identifier", "routine_body", "routine_definition",
                "external_name", "external_language", "parameter_style",
                "is_deterministic", "sql_data_access", "is_null_call", "sql_path",
                "schema_level_routine", "max_dynamic_result_sets",
                "is_user_defined_cast", "is_implicitly_invocable", "security_type",
                "to_sql_specific_catalog", "to_sql_specific_schema", "to_sql_specific_name",
                "as_locator", "created", "last_altered", "new", "defined",
                "security_definer", "is_udt_dependent"
            ],
            ["parameters"] =
            [
                "specific_catalog", "specific_schema", "specific_name",
                "ordinal_position", "parameter_mode", "is_result", "as_locator",
                "parameter_name", "data_type",
                "character_maximum_length", "character_octet_length",
                "character_set_catalog", "character_set_schema", "character_set_name",
                "collation_catalog", "collation_schema", "collation_name",
                "numeric_precision", "numeric_precision_radix", "numeric_scale",
                "datetime_precision", "interval_type", "interval_precision",
                "udt_catalog", "udt_schema", "udt_name",
                "scope_catalog", "scope_schema", "scope_name", "maximum_cardinality",
                "dtd_identifier", "parameter_default"
            ],
        };

    /// <summary>
    /// Executes a SELECT against one of the <c>information_schema</c> metadata views.
    /// </summary>
    /// <param name="sql">The complete SELECT statement.</param>
    /// <param name="viewName">The view name taken from the FROM clause.</param>
    /// <returns>The projected, filtered and ordered rows.</returns>
    /// <exception cref="InvalidOperationException">Thrown for an unknown metadata view.</exception>
    /// <exception cref="NotSupportedException">Thrown for a predicate or projection shape this layer does not implement.</exception>
    private List<Dictionary<string, object>> ExecuteInformationSchemaQuery(string sql, string viewName)
    {
        var catalog = ResolveInformationSchemaCatalogName();
        var (rows, declaredColumns) = BuildInformationSchemaRows(viewName, catalog);

        var groupPosition = IndexOfTopLevelKeyword(sql, "GROUP", 0);
        var orderPosition = IndexOfTopLevelKeyword(sql, "ORDER", 0);
        var limitPosition = IndexOfTopLevelKeyword(sql, "LIMIT", 0);
        var offsetPosition = IndexOfTopLevelKeyword(sql, "OFFSET", 0);

        // WHERE — evaluated against the source columns, before projection (SQL evaluation order).
        var wherePosition = IndexOfTopLevelKeyword(sql, "WHERE", 0);
        if (wherePosition >= 0)
        {
            var whereStart = wherePosition + "WHERE".Length;
            var whereEnd = FirstClauseStart(whereStart, sql.Length, [groupPosition, orderPosition, limitPosition, offsetPosition]);
            var predicate = sql[whereStart..whereEnd].Trim();
            if (predicate.Length > 0)
            {
                rows = [.. rows.Where(row => EvaluateInformationSchemaPredicate(predicate, row))];
            }
        }

        var selectClause = ExtractInformationSchemaSelectClause(sql);

        // COUNT(*) is answered here because the generic aggregate path would look for a real table.
        if (selectClause.Contains("COUNT(*)", StringComparison.OrdinalIgnoreCase))
        {
            if (groupPosition >= 0)
                throw new NotSupportedException("information_schema: GROUP BY is not supported");

            return [new Dictionary<string, object> { [ExtractCountAlias(selectClause) ?? "cnt"] = (long)rows.Count }];
        }

        var projection = ParseInformationSchemaProjection(selectClause, declaredColumns);

        // ORDER BY — resolved against the source columns first, then against the projected aliases.
        rows = ApplyInformationSchemaOrdering(sql, rows, projection, declaredColumns);

        // LIMIT / OFFSET
        rows = ApplyInformationSchemaLimitOffset(sql, rows);

        return ProjectInformationSchemaRows(rows, projection, declaredColumns);
    }

    /// <summary>Builds the unfiltered rows of a metadata view plus its declared column list.</summary>
    private (List<Dictionary<string, object>> Rows, string[] Columns) BuildInformationSchemaRows(string viewName, string catalog) =>
        viewName.ToLowerInvariant() switch
        {
            "tables" => (BuildInformationSchemaTablesRows(catalog), InformationSchemaTablesColumns),
            "views" => (BuildInformationSchemaViewsRows(catalog), InformationSchemaViewsColumns),
            "columns" => (BuildInformationSchemaColumnsRows(catalog), InformationSchemaColumnsColumns),
            "schemata" => (BuildInformationSchemaSchemataRows(catalog), InformationSchemaSchemataColumns),
            "indexes" => (BuildInformationSchemaIndexesRows(catalog), InformationSchemaIndexesColumns),
            "triggers" => (BuildInformationSchemaTriggersRows(catalog), InformationSchemaTriggersColumns),
            _ when InformationSchemaEmptyViews.TryGetValue(viewName, out var emptyColumns) => ([], emptyColumns),
            _ => throw new InvalidOperationException($"{InformationSchemaSchemaName}.{viewName} does not exist"),
        };

    /// <summary>Creates a row carrying every declared column, so result shapes stay stable.</summary>
    private static Dictionary<string, object> NewInformationSchemaRow(string[] columns)
    {
        var row = new Dictionary<string, object>(columns.Length, StringComparer.OrdinalIgnoreCase);
        foreach (var column in columns)
            row[column] = null!;

        return row;
    }

    /// <summary>Resolves the catalog name reported in metadata rows from the database path.</summary>
    private string ResolveInformationSchemaCatalogName()
    {
        if (string.IsNullOrWhiteSpace(this.dbPath))
            return InformationSchemaFallbackCatalog;

        var trimmed = this.dbPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileNameWithoutExtension(trimmed);
        return string.IsNullOrWhiteSpace(name) ? InformationSchemaFallbackCatalog : name;
    }

    /// <summary>The first clause keyword position at or after <paramref name="startIndex"/>, or the end of the SQL.</summary>
    private static int FirstClauseStart(int startIndex, int sqlLength, int[] candidates)
    {
        var end = sqlLength;
        foreach (var candidate in candidates)
        {
            if (candidate > startIndex && candidate < end)
                end = candidate;
        }

        return end;
    }

    /// <summary>The SELECT list text — everything between SELECT and the FROM clause.</summary>
    private static string ExtractInformationSchemaSelectClause(string sql)
    {
        var selectPosition = IndexOfTopLevelKeyword(sql, "SELECT", 0);
        var fromPosition = IndexOfTopLevelKeyword(sql, "FROM", selectPosition < 0 ? 0 : selectPosition + "SELECT".Length);
        if (selectPosition < 0 || fromPosition < 0)
            throw new NotSupportedException("information_schema requires a SELECT ... FROM statement");

        return sql[(selectPosition + "SELECT".Length)..fromPosition].Trim();
    }

    /// <summary>
    /// Finds the position of a top-level keyword: outside string literals and at parenthesis depth 0,
    /// with identifier boundaries on both sides. Returns -1 when the keyword is absent.
    /// </summary>
    private static int IndexOfTopLevelKeyword(string sql, string keyword, int startIndex)
    {
        if (startIndex < 0)
            startIndex = 0;

        var depth = 0;
        var inLiteral = false;
        for (var i = startIndex; i < sql.Length; i++)
        {
            var c = sql[i];
            if (inLiteral)
            {
                if (c != '\'')
                    continue;

                // '' is an escaped quote inside a literal
                if (i + 1 < sql.Length && sql[i + 1] == '\'')
                {
                    i++;
                    continue;
                }

                inLiteral = false;
                continue;
            }

            if (c == '\'')
            {
                inLiteral = true;
                continue;
            }

            if (c == '(')
            {
                depth++;
                continue;
            }

            if (c == ')')
            {
                depth--;
                continue;
            }

            if (depth == 0 && IsKeywordAt(sql, keyword, i))
                return i;
        }

        return -1;
    }

    /// <summary>True when <paramref name="keyword"/> sits at <paramref name="position"/> with identifier boundaries.</summary>
    private static bool IsKeywordAt(string sql, string keyword, int position)
    {
        if (position + keyword.Length > sql.Length)
            return false;

        if (string.Compare(sql, position, keyword, 0, keyword.Length, StringComparison.OrdinalIgnoreCase) != 0)
            return false;

        // Boundaries only matter for word-like keywords; a separator such as ',' may touch identifier
        // characters on both sides (`table_name, table_type`).
        if (IsIdentifierCharacter(keyword[0]) && position > 0 && IsIdentifierCharacter(sql[position - 1]))
            return false;

        var after = position + keyword.Length;
        return !IsIdentifierCharacter(keyword[^1]) || after >= sql.Length || !IsIdentifierCharacter(sql[after]);
    }

    /// <summary>Characters that may appear inside an identifier (used for keyword boundaries).</summary>
    private static bool IsIdentifierCharacter(char c) => char.IsLetterOrDigit(c) || c is '_' or '$' or '@';

    /// <summary>Splits on a top-level keyword (used for <c>AND</c>/<c>OR</c>/<c>,</c>).</summary>
    private static List<string> SplitTopLevel(string text, string keyword)
    {
        var parts = new List<string>();
        var start = 0;
        while (true)
        {
            var position = IndexOfTopLevelKeyword(text, keyword, start);
            if (position < 0)
                break;

            parts.Add(text[start..position]);
            start = position + keyword.Length;
        }

        parts.Add(text[start..]);
        return parts;
    }

    /// <summary>True when the whole atom is wrapped in one pair of parentheses.</summary>
    private static bool IsFullyParenthesised(string atom)
    {
        if (atom.Length < 2 || atom[0] != '(' || atom[^1] != ')')
            return false;

        var depth = 0;
        var inLiteral = false;
        for (var i = 0; i < atom.Length; i++)
        {
            var c = atom[i];
            if (inLiteral)
            {
                if (c != '\'')
                    continue;

                if (i + 1 < atom.Length && atom[i + 1] == '\'')
                {
                    i++;
                    continue;
                }

                inLiteral = false;
                continue;
            }

            if (c == '\'')
            {
                inLiteral = true;
                continue;
            }

            if (c == '(')
            {
                depth++;
                continue;
            }

            if (c == ')')
            {
                depth--;
                if (depth == 0 && i != atom.Length - 1)
                    return false;
            }
        }

        return depth == 0;
    }

    /// <summary>Drops a table/view qualifier and quoting from an identifier.</summary>
    private static string StripIdentifierQualifier(string identifier)
    {
        var trimmed = identifier.Trim();
        var lastDot = trimmed.LastIndexOf('.');
        if (lastDot >= 0)
            trimmed = trimmed[(lastDot + 1)..];

        return trimmed.Trim().Trim('"', '`', '[', ']');
    }

    /// <summary>Base tables (<c>table_type = 'BASE TABLE'</c>) followed by views (<c>'VIEW'</c>).</summary>
    private List<Dictionary<string, object>> BuildInformationSchemaTablesRows(string catalog)
    {
        var rows = new List<Dictionary<string, object>>(this.tables.Count);

        foreach (var tableName in this.tables.Keys.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase))
            rows.Add(NewInformationSchemaTableRow(InformationSchemaTablesColumns, catalog, tableName, "BASE TABLE"));

        foreach (var viewName in GetViewNames().OrderBy(static name => name, StringComparer.OrdinalIgnoreCase))
            rows.Add(NewInformationSchemaTableRow(InformationSchemaTablesColumns, catalog, viewName, "VIEW"));

        return rows;
    }

    /// <summary>One <c>information_schema.tables</c> row.</summary>
    private static Dictionary<string, object> NewInformationSchemaTableRow(
        string[] columns, string catalog, string objectName, string objectType)
    {
        var row = NewInformationSchemaRow(columns);
        row["table_catalog"] = catalog;
        row["table_schema"] = InformationSchemaDefaultSchema;
        row["table_name"] = objectName;
        row["table_type"] = objectType;

        // Base tables are writable through the normal write path; a view is read-only.
        var isBaseTable = objectType.Equals("BASE TABLE", StringComparison.Ordinal);
        row["is_insertable_into"] = isBaseTable ? "YES" : "NO";
        row["is_typed"] = "NO";
        return row;
    }

    /// <summary>Registered views with their SELECT definition.</summary>
    private List<Dictionary<string, object>> BuildInformationSchemaViewsRows(string catalog)
    {
        var rows = new List<Dictionary<string, object>>();
        foreach (var viewName in GetViewNames().OrderBy(static name => name, StringComparer.OrdinalIgnoreCase))
        {
            var definition = TryGetView(viewName);
            var row = NewInformationSchemaRow(InformationSchemaViewsColumns);
            row["table_catalog"] = catalog;
            row["table_schema"] = InformationSchemaDefaultSchema;
            row["table_name"] = viewName;
            row["view_definition"] = definition?.SelectQuery ?? null!;
            row["check_option"] = "NONE";
            row["is_updatable"] = "NO";
            row["is_insertable_into"] = "NO";
            row["is_trigger_updatable"] = "NO";
            row["is_trigger_deletable"] = "NO";
            row["is_trigger_insertable_into"] = "NO";
            rows.Add(row);
        }

        return rows;
    }

    /// <summary>The schemas this engine exposes: <c>public</c>, <c>information_schema</c> and <c>pg_catalog</c>.</summary>
    private static List<Dictionary<string, object>> BuildInformationSchemaSchemataRows(string catalog)
    {
        var rows = new List<Dictionary<string, object>>(3);
        foreach (var schemaName in (string[])[InformationSchemaDefaultSchema, InformationSchemaSchemaName, "pg_catalog"])
        {
            var row = NewInformationSchemaRow(InformationSchemaSchemataColumns);
            row["catalog_name"] = catalog;
            row["schema_name"] = schemaName;
            row["schema_owner"] = "admin";
            rows.Add(row);
        }

        return rows;
    }

    /// <summary>One row per column of every base table (views carry no static column list).</summary>
    private List<Dictionary<string, object>> BuildInformationSchemaColumnsRows(string catalog)
    {
        var rows = new List<Dictionary<string, object>>();
        foreach (var tableName in this.tables.Keys.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase))
        {
            var table = this.tables[tableName];
            var concrete = table as Table;

            for (var index = 0; index < table.Columns.Count; index++)
            {
                var dataType = index < table.ColumnTypes.Count ? table.ColumnTypes[index] : DataType.String;
                var row = NewInformationSchemaRow(InformationSchemaColumnsColumns);
                row["table_catalog"] = catalog;
                row["table_schema"] = InformationSchemaDefaultSchema;
                row["table_name"] = tableName;
                row["column_name"] = table.Columns[index];
                row["ordinal_position"] = index + 1;
                row["column_default"] = concrete is not null && index < concrete.DefaultValues.Count
                    ? concrete.DefaultValues[index]?.ToString() ?? null!
                    : null!;
                row["is_nullable"] = concrete is not null && index < concrete.IsNotNull.Count && concrete.IsNotNull[index]
                    ? "NO"
                    : "YES";
                row["data_type"] = MapInformationSchemaDataType(dataType);
                row["udt_catalog"] = catalog;
                row["udt_schema"] = "pg_catalog";
                row["udt_name"] = MapInformationSchemaUdtName(dataType);
                row["dtd_identifier"] = (index + 1).ToString();
                row["is_self_referencing"] = "NO";
                row["is_identity"] = "NO";
                row["identity_cycle"] = "NO";
                row["is_generated"] = "NEVER";
                row["is_updatable"] = "YES";
                rows.Add(row);
            }
        }

        return rows;
    }

    /// <summary>Registered hash/B-tree indexes plus the vector indexes stored in table metadata.</summary>
    private List<Dictionary<string, object>> BuildInformationSchemaIndexesRows(string catalog)
    {
        var rows = new List<Dictionary<string, object>>();
        foreach (var tableName in this.tables.Keys.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase))
        {
            var table = this.tables[tableName];
            var primaryKeyColumn = table.PrimaryKeyIndex >= 0 && table.PrimaryKeyIndex < table.Columns.Count
                ? table.Columns[table.PrimaryKeyIndex]
                : null;

            if (table is Table concrete)
            {
                // Locked snapshot: index name, indexed column, kind and uniqueness.
                foreach (var (indexName, columnName, indexType, isUnique) in concrete.GetIndexCatalogSnapshot())
                {
                    rows.Add(NewInformationSchemaIndexRow(
                        catalog, tableName, indexName, columnName, indexType, isUnique,
                        columnName.Equals(primaryKeyColumn, StringComparison.OrdinalIgnoreCase)));
                }
            }
            else
            {
                // Other ITable implementations expose the per-column hash-index probe only.
                foreach (var columnName in table.Columns)
                {
                    if (!table.HasHashIndex(columnName))
                        continue;

                    rows.Add(NewInformationSchemaIndexRow(
                        catalog, tableName, columnName, columnName, "HASH", false,
                        columnName.Equals(primaryKeyColumn, StringComparison.OrdinalIgnoreCase)));
                }
            }

            // CREATE VECTOR INDEX stores name/type/sql on the table as metadata (see ExecuteCreateVectorIndex).
            foreach (var columnName in table.Columns)
            {
                if (table.GetMetadata($"vector_index:{columnName}:name") is not string vectorIndexName)
                    continue;

                var vectorIndexType = table.GetMetadata($"vector_index:{columnName}:type") as string ?? "FLAT";
                rows.Add(NewInformationSchemaIndexRow(
                    catalog, tableName, vectorIndexName, columnName, vectorIndexType, false, false));
            }
        }

        return rows;
    }

    /// <summary>One <c>information_schema.indexes</c> row.</summary>
    private static Dictionary<string, object> NewInformationSchemaIndexRow(
        string catalog, string tableName, string indexName, string columnName,
        string indexType, bool isUnique, bool isPrimary)
    {
        var row = NewInformationSchemaRow(InformationSchemaIndexesColumns);
        row["table_catalog"] = catalog;
        row["table_schema"] = InformationSchemaDefaultSchema;
        row["table_name"] = tableName;
        row["index_name"] = indexName;
        row["column_name"] = columnName;
        row["index_type"] = indexType;
        row["is_unique"] = isUnique ? "YES" : "NO";
        row["is_primary"] = isPrimary ? "YES" : "NO";
        return row;
    }

    /// <summary>Registered triggers with their timing, event and body.</summary>
    private List<Dictionary<string, object>> BuildInformationSchemaTriggersRows(string catalog)
    {
        var rows = new List<Dictionary<string, object>>();
        lock (_triggerLock)
        {
            foreach (var trigger in _triggers.Values.OrderBy(static definition => definition.Name, StringComparer.OrdinalIgnoreCase))
            {
                var row = NewInformationSchemaRow(InformationSchemaTriggersColumns);
                row["trigger_catalog"] = catalog;
                row["trigger_schema"] = InformationSchemaDefaultSchema;
                row["trigger_name"] = trigger.Name;
                row["event_manipulation"] = trigger.Event.ToString().ToUpperInvariant();
                row["event_object_catalog"] = catalog;
                row["event_object_schema"] = InformationSchemaDefaultSchema;
                row["event_object_table"] = trigger.TableName;
                row["action_order"] = 1;
                row["action_statement"] = trigger.Body;
                row["action_orientation"] = "ROW";
                row["action_timing"] = trigger.Timing.ToString().ToUpperInvariant();
                rows.Add(row);
            }
        }

        return rows;
    }

    /// <summary>Engine <see cref="DataType"/> → SQL-standard <c>data_type</c> name (server-catalog compatible).</summary>
    private static string MapInformationSchemaDataType(DataType dataType) => dataType switch
    {
        DataType.Integer => "integer",
        DataType.Long => "bigint",
        DataType.Real => "double precision",
        DataType.Decimal => "numeric",
        DataType.Boolean => "boolean",
        DataType.String => "text",
        DataType.Blob => "bytea",
        DataType.DateTime => "timestamp without time zone",
        DataType.Guid => "uuid",
        DataType.Ulid => "text",
        DataType.RowRef => "text",
        DataType.Vector => "vector",
        _ => "text",
    };

    /// <summary>Engine <see cref="DataType"/> → PostgreSQL <c>udt_name</c>.</summary>
    private static string MapInformationSchemaUdtName(DataType dataType) => dataType switch
    {
        DataType.Integer => "int4",
        DataType.Long => "int8",
        DataType.Real => "float8",
        DataType.Decimal => "numeric",
        DataType.Boolean => "bool",
        DataType.Blob => "bytea",
        DataType.DateTime => "timestamp",
        DataType.Guid => "uuid",
        _ => "text",
    };

    /// <summary>Maps each SELECT-list item to a (source column, output name) pair.</summary>
    private static List<(string Source, string Output)> ParseInformationSchemaProjection(
        string selectClause, string[] declaredColumns)
    {
        var projection = new List<(string Source, string Output)>();
        foreach (var item in SplitTopLevel(selectClause, ","))
        {
            var text = item.Trim();
            if (text.Length == 0)
                continue;

            if (text == "*")
            {
                foreach (var declared in declaredColumns)
                    projection.Add((declared, declared));

                continue;
            }

            var aliasPosition = IndexOfTopLevelKeyword(text, "AS", 0);
            string source;
            string output;
            if (aliasPosition > 0)
            {
                source = text[..aliasPosition].Trim();
                output = StripIdentifierQualifier(text[(aliasPosition + "AS".Length)..]);
            }
            else
            {
                var spacePosition = text.IndexOf(' ');
                if (spacePosition > 0)
                {
                    source = text[..spacePosition].Trim();
                    output = StripIdentifierQualifier(text[(spacePosition + 1)..]);
                }
                else
                {
                    source = text;
                    output = text;
                }
            }

            source = StripIdentifierQualifier(source);
            if (!declaredColumns.Contains(source, StringComparer.OrdinalIgnoreCase))
                throw new NotSupportedException($"information_schema: column '{source}' is not part of this view");

            projection.Add((source, output));
        }

        if (projection.Count == 0)
        {
            foreach (var declared in declaredColumns)
                projection.Add((declared, declared));
        }

        return projection;
    }

    /// <summary>Applies <c>ORDER BY</c> against the source columns, the projected aliases or an ordinal.</summary>
    private static List<Dictionary<string, object>> ApplyInformationSchemaOrdering(
        string sql,
        List<Dictionary<string, object>> rows,
        List<(string Source, string Output)> projection,
        string[] declaredColumns)
    {
        var orderPosition = IndexOfTopLevelKeyword(sql, "ORDER", 0);
        if (orderPosition < 0)
            return rows;

        var byPosition = IndexOfTopLevelKeyword(sql, "BY", orderPosition + "ORDER".Length);
        if (byPosition < 0)
            return rows;

        var start = byPosition + "BY".Length;
        var end = FirstClauseStart(start, sql.Length,
            [IndexOfTopLevelKeyword(sql, "LIMIT", start), IndexOfTopLevelKeyword(sql, "OFFSET", start)]);
        var orderText = sql[start..end].Trim();
        if (orderText.Length == 0)
            return rows;

        IOrderedEnumerable<Dictionary<string, object>>? ordered = null;
        foreach (var item in SplitTopLevel(orderText, ","))
        {
            var spec = item.Trim();
            if (spec.Length == 0)
                continue;

            var descending = spec.EndsWith(" DESC", StringComparison.OrdinalIgnoreCase);
            if (descending || spec.EndsWith(" ASC", StringComparison.OrdinalIgnoreCase))
                spec = spec[..spec.LastIndexOf(' ')].Trim();

            var column = ResolveInformationSchemaOrderColumn(spec, projection, declaredColumns);
            object? KeySelector(Dictionary<string, object> row) => row.TryGetValue(column, out var value) ? value : null;

            ordered = ordered is null
                ? descending
                    ? rows.OrderByDescending(KeySelector, InformationSchemaValueComparer.Instance)
                    : rows.OrderBy(KeySelector, InformationSchemaValueComparer.Instance)
                : descending
                    ? ordered.ThenByDescending(KeySelector, InformationSchemaValueComparer.Instance)
                    : ordered.ThenBy(KeySelector, InformationSchemaValueComparer.Instance);
        }

        return ordered?.ToList() ?? rows;
    }

    /// <summary>Resolves an ORDER BY term: ordinal, projected column/alias, or any declared column.</summary>
    private static string ResolveInformationSchemaOrderColumn(
        string spec, List<(string Source, string Output)> projection, string[] declaredColumns)
    {
        if (int.TryParse(spec, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ordinal)
            && ordinal >= 1 && ordinal <= projection.Count)
        {
            return projection[ordinal - 1].Source;
        }

        var name = StripIdentifierQualifier(spec);
        foreach (var (source, _) in projection)
        {
            if (source.Equals(name, StringComparison.OrdinalIgnoreCase))
                return source;
        }

        foreach (var (source, output) in projection)
        {
            if (output.Equals(name, StringComparison.OrdinalIgnoreCase))
                return source;
        }

        // Ordering happens before projection, so a declared column that the SELECT list omits is
        // still a valid sort key.
        foreach (var declared in declaredColumns)
        {
            if (declared.Equals(name, StringComparison.OrdinalIgnoreCase))
                return declared;
        }

        throw new NotSupportedException($"information_schema: ORDER BY '{name}' is not part of this view");
    }

    /// <summary>Applies <c>LIMIT</c> (also the SQLite <c>LIMIT offset, count</c> form) and <c>OFFSET</c>.</summary>
    private static List<Dictionary<string, object>> ApplyInformationSchemaLimitOffset(
        string sql, List<Dictionary<string, object>> rows)
    {
        var limitPosition = IndexOfTopLevelKeyword(sql, "LIMIT", 0);
        var offsetPosition = IndexOfTopLevelKeyword(sql, "OFFSET", 0);
        if (limitPosition < 0 && offsetPosition < 0)
            return rows;

        int? limit = null;
        int? offset = null;

        if (limitPosition >= 0)
        {
            var start = limitPosition + "LIMIT".Length;
            var end = FirstClauseStart(start, sql.Length, [offsetPosition]);
            var text = sql[start..end].Trim().TrimEnd(';');
            var comma = text.IndexOf(',');
            if (comma >= 0)
            {
                offset = ParseInformationSchemaInteger(text[..comma].Trim(), "LIMIT");
                limit = ParseInformationSchemaInteger(text[(comma + 1)..].Trim(), "LIMIT");
            }
            else
            {
                limit = ParseInformationSchemaInteger(text, "LIMIT");
            }
        }

        if (offsetPosition >= 0)
        {
            var text = sql[(offsetPosition + "OFFSET".Length)..].Trim().TrimEnd(';');
            var space = text.IndexOf(' ');
            if (space > 0)
                text = text[..space];

            offset = ParseInformationSchemaInteger(text, "OFFSET");
        }

        if (offset is > 0)
            rows = [.. rows.Skip(offset.Value)];

        if (limit is >= 0)
            rows = [.. rows.Take(limit.Value)];

        return rows;
    }

    /// <summary>Parses a LIMIT/OFFSET operand, failing loudly instead of guessing.</summary>
    private static int ParseInformationSchemaInteger(string text, string clause) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new NotSupportedException($"information_schema: {clause} expects an integer (got '{text}')");

    /// <summary>Reduces every row to the requested columns, in the requested order.</summary>
    private static List<Dictionary<string, object>> ProjectInformationSchemaRows(
        List<Dictionary<string, object>> rows,
        List<(string Source, string Output)> projection,
        string[] declaredColumns)
    {
        var isDeclaredOrder = projection.Count == declaredColumns.Length;
        if (isDeclaredOrder)
        {
            for (var index = 0; index < declaredColumns.Length; index++)
            {
                if (!projection[index].Source.Equals(declaredColumns[index], StringComparison.OrdinalIgnoreCase)
                    || !projection[index].Output.Equals(declaredColumns[index], StringComparison.OrdinalIgnoreCase))
                {
                    isDeclaredOrder = false;
                    break;
                }
            }
        }

        var result = new List<Dictionary<string, object>>(rows.Count);
        foreach (var row in rows)
        {
            // `SELECT *` keeps the declared column instances untouched (no per-row copy needed).
            if (isDeclaredOrder && projection.Count == row.Count)
            {
                result.Add(row);
                continue;
            }

            var projected = new Dictionary<string, object>(projection.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var (source, output) in projection)
                projected[output] = row.TryGetValue(source, out var value) ? value : null!;

            result.Add(projected);
        }

        return result;
    }

    /// <summary>Orders metadata values: strings case-insensitively, numbers numerically, nulls first.</summary>
    private sealed class InformationSchemaValueComparer : IComparer<object?>
    {
        /// <summary>Shared instance — the comparer is stateless.</summary>
        internal static readonly InformationSchemaValueComparer Instance = new();

        /// <inheritdoc />
        public int Compare(object? x, object? y)
        {
            if (ReferenceEquals(x, y))
                return 0;

            if (x is null or DBNull)
                return -1;

            if (y is null or DBNull)
                return 1;

            if (x is string leftText && y is string rightText)
                return string.Compare(leftText, rightText, StringComparison.OrdinalIgnoreCase);

            if (x is IComparable comparable && x.GetType() == y.GetType())
                return comparable.CompareTo(y);

            return string.Compare(x.ToString(), y.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>The alias of a <c>COUNT(*)</c> item, when the statement names one.</summary>
    private static string? ExtractCountAlias(string selectClause)
    {
        var position = selectClause.IndexOf("COUNT(*)", StringComparison.OrdinalIgnoreCase);
        if (position < 0)
            return null;

        var tail = selectClause[(position + "COUNT(*)".Length)..].Trim();
        if (tail.Length == 0)
            return null;

        var asPosition = IndexOfTopLevelKeyword(tail, "AS", 0);
        var candidate = (asPosition >= 0 ? tail[(asPosition + "AS".Length)..] : tail).Trim().Trim('"', '`', '[', ']');
        var space = candidate.IndexOf(' ');
        if (space > 0)
            candidate = candidate[..space];

        return candidate.Length == 0 ? null : candidate;
    }

    /// <summary>Evaluates a WHERE predicate over a metadata row (OR of AND-groups, parens allowed).</summary>
    private static bool EvaluateInformationSchemaPredicate(string predicate, Dictionary<string, object> row)
    {
        foreach (var orPart in SplitTopLevel(predicate, "OR"))
        {
            var isMatch = true;
            foreach (var andPart in SplitTopLevel(orPart, "AND"))
            {
                var atom = andPart.Trim();
                if (atom.Length == 0)
                    continue;

                var matches = IsFullyParenthesised(atom)
                    ? EvaluateInformationSchemaPredicate(atom[1..^1], row)
                    : EvaluateInformationSchemaAtom(atom, row);

                if (matches)
                    continue;

                isMatch = false;
                break;
            }

            if (isMatch)
                return true;
        }

        return false;
    }

    /// <summary>Evaluates a single comparison atom such as <c>table_name LIKE 'a%'</c>.</summary>
    private static bool EvaluateInformationSchemaAtom(string atom, Dictionary<string, object> row)
    {
        if (atom.StartsWith("NOT ", StringComparison.OrdinalIgnoreCase))
            return !EvaluateInformationSchemaPredicate(atom["NOT ".Length..], row);

        var isNotNullPosition = IndexOfTopLevelKeyword(atom, "IS NOT NULL", 0);
        if (isNotNullPosition >= 0)
            return ResolveInformationSchemaColumn(atom[..isNotNullPosition], row) is not null;

        var isNullPosition = IndexOfTopLevelKeyword(atom, "IS NULL", 0);
        if (isNullPosition >= 0)
            return ResolveInformationSchemaColumn(atom[..isNullPosition], row) is null;

        var notLikePosition = IndexOfTopLevelKeyword(atom, "NOT LIKE", 0);
        if (notLikePosition >= 0)
        {
            var value = ResolveInformationSchemaColumn(atom[..notLikePosition], row);
            return !MatchInformationSchemaLike(value, ParseInformationSchemaLiteral(atom[(notLikePosition + "NOT LIKE".Length)..]));
        }

        var likePosition = IndexOfTopLevelKeyword(atom, "LIKE", 0);
        if (likePosition >= 0)
        {
            var value = ResolveInformationSchemaColumn(atom[..likePosition], row);
            return MatchInformationSchemaLike(value, ParseInformationSchemaLiteral(atom[(likePosition + "LIKE".Length)..]));
        }

        var notInPosition = IndexOfTopLevelKeyword(atom, "NOT IN", 0);
        if (notInPosition >= 0)
        {
            var value = ResolveInformationSchemaColumn(atom[..notInPosition], row);
            return !EvaluateInformationSchemaInList(value, atom[(notInPosition + "NOT IN".Length)..]);
        }

        var inPosition = IndexOfTopLevelKeyword(atom, "IN", 0);
        if (inPosition >= 0)
        {
            var value = ResolveInformationSchemaColumn(atom[..inPosition], row);
            return EvaluateInformationSchemaInList(value, atom[(inPosition + "IN".Length)..]);
        }

        // Two-character operators are probed before their prefixes so `>=` never reads as `>`.
        foreach (var comparisonOperator in (string[])["<>", "!=", "<=", ">=", "=", "<", ">"])
        {
            var operatorPosition = FindTopLevelOperator(atom, comparisonOperator);
            if (operatorPosition < 0)
                continue;

            var left = ResolveInformationSchemaColumn(atom[..operatorPosition], row);
            var right = ParseInformationSchemaLiteral(atom[(operatorPosition + comparisonOperator.Length)..]);
            return CompareInformationSchemaValues(left, right, comparisonOperator);
        }

        throw new NotSupportedException($"information_schema: unsupported predicate '{atom}'");
    }

    /// <summary>Reads a column value from a metadata row, rejecting columns the view does not expose.</summary>
    private static object? ResolveInformationSchemaColumn(string text, Dictionary<string, object> row)
    {
        var column = StripIdentifierQualifier(text);
        if (column.Length == 0 || !row.TryGetValue(column, out var value))
            throw new NotSupportedException($"information_schema: column '{text.Trim()}' is not part of this view");

        return value is null or DBNull ? null : value;
    }

    /// <summary>Parses a string, numeric, boolean or NULL literal.</summary>
    private static object? ParseInformationSchemaLiteral(string text)
    {
        var literal = text.Trim().TrimEnd(';');
        if (literal.Length == 0)
            throw new NotSupportedException("information_schema: missing value in predicate");

        if (literal.Length >= 2 && literal[0] == '\'' && literal[^1] == '\'')
            return literal[1..^1].Replace("''", "'", StringComparison.Ordinal);

        if (literal.Equals("NULL", StringComparison.OrdinalIgnoreCase))
            return null;

        if (literal.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
            return true;

        if (literal.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
            return false;

        if (double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return number;

        throw new NotSupportedException($"information_schema: unsupported literal '{literal}'");
    }

    /// <summary>Matches a metadata value against a SQL <c>LIKE</c> pattern (<c>%</c> and <c>_</c>).</summary>
    private static bool MatchInformationSchemaLike(object? value, object? pattern)
    {
        if (value is null || pattern is not string patternText)
            return false;

        var regex = new StringBuilder("^");
        foreach (var c in patternText)
        {
            switch (c)
            {
                case '%':
                    regex.Append(".*");
                    break;
                case '_':
                    regex.Append('.');
                    break;
                default:
                    regex.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }

        regex.Append('$');
        return Regex.IsMatch(
            value.ToString() ?? string.Empty,
            regex.ToString(),
            RegexOptions.IgnoreCase | RegexOptions.Singleline,
            TimeSpan.FromSeconds(1));
    }

    /// <summary>Evaluates <c>col IN (a, b, …)</c> for literal lists.</summary>
    private static bool EvaluateInformationSchemaInList(object? value, string listText)
    {
        var inner = listText.Trim();
        if (inner.StartsWith('(') && inner.EndsWith(')'))
            inner = inner[1..^1];

        foreach (var item in SplitTopLevel(inner, ","))
        {
            var literal = ParseInformationSchemaLiteral(item);
            if (CompareInformationSchemaValues(value, literal, "="))
                return true;
        }

        return false;
    }

    /// <summary>SQL comparison semantics: a NULL operand never satisfies <c>=</c>/<c>&lt;</c>/…</summary>
    private static bool CompareInformationSchemaValues(object? left, object? right, string comparisonOperator)
    {
        // Three-valued logic: UNKNOWN is not TRUE, so a NULL comparison filters the row out.
        if (left is null || right is null)
            return false;

        var comparison = CompareInformationSchemaRawValues(left, right);
        return comparisonOperator switch
        {
            "=" => comparison == 0,
            "<>" or "!=" => comparison != 0,
            "<" => comparison < 0,
            "<=" => comparison <= 0,
            ">" => comparison > 0,
            ">=" => comparison >= 0,
            _ => throw new NotSupportedException($"information_schema: unsupported operator '{comparisonOperator}'"),
        };
    }

    /// <summary>Compares two non-null metadata values: numeric when both are numbers, else text.</summary>
    private static int CompareInformationSchemaRawValues(object left, object right)
    {
        if (left is string leftText && right is string rightText)
            return string.Compare(leftText, rightText, StringComparison.OrdinalIgnoreCase);

        if (IsInformationSchemaNumber(left, out var leftNumber) && IsInformationSchemaNumber(right, out var rightNumber))
            return leftNumber.CompareTo(rightNumber);

        return string.Compare(left.ToString(), right.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True for the numeric value kinds this layer uses (ordinals, LIMIT operands).</summary>
    private static bool IsInformationSchemaNumber(object value, out double number)
    {
        switch (value)
        {
            case int intValue:
                number = intValue;
                return true;
            case long longValue:
                number = longValue;
                return true;
            case double doubleValue:
                number = doubleValue;
                return true;
            case decimal decimalValue:
                number = (double)decimalValue;
                return true;
            default:
                number = 0;
                return false;
        }
    }

    /// <summary>Finds a comparison operator outside literals and parentheses.</summary>
    private static int FindTopLevelOperator(string text, string comparisonOperator)
    {
        var depth = 0;
        var inLiteral = false;
        for (var i = 0; i + comparisonOperator.Length <= text.Length; i++)
        {
            var c = text[i];
            if (inLiteral)
            {
                if (c != '\'')
                    continue;

                if (i + 1 < text.Length && text[i + 1] == '\'')
                {
                    i++;
                    continue;
                }

                inLiteral = false;
                continue;
            }

            if (c == '\'')
            {
                inLiteral = true;
                continue;
            }

            if (c == '(')
            {
                depth++;
                continue;
            }

            if (c == ')')
            {
                depth--;
                continue;
            }

            if (depth != 0 || c != comparisonOperator[0])
                continue;

            if (string.CompareOrdinal(text, i, comparisonOperator, 0, comparisonOperator.Length) == 0)
                return i;
        }

        return -1;
    }
}
