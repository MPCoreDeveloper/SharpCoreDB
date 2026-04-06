// <copyright file="PgCatalogService.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.Extensions.Logging;
using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;
using System.Text.RegularExpressions;

namespace SharpCoreDB.Server.Core.Catalog;

/// <summary>
/// Intercepts PostgreSQL catalog queries (pg_catalog, information_schema) and
/// returns synthetic metadata rows derived from the live database schema.
/// Enables GUI tools (DBeaver, DataGrip, pgAdmin, Beekeeper) to discover
/// tables, columns, types, and constraints without requiring native catalog storage.
/// C# 14: Primary constructor, collection expressions, switch expression.
/// </summary>
public sealed class PgCatalogService(ILogger<PgCatalogService> logger)
{
    // Server version advertised to clients
    private const string ServerVersion = "SharpCoreDB 1.6.0 on .NET 10 (PostgreSQL protocol compatible)";

    // Catalog table/view names that this service handles
    private static readonly HashSet<string> KnownCatalogSources = new(StringComparer.OrdinalIgnoreCase)
    {
        "information_schema.tables",
        "information_schema.columns",
        "information_schema.schemata",
        "information_schema.table_constraints",
        "information_schema.key_column_usage",
        "information_schema.referential_constraints",
        "information_schema.constraint_column_usage",
        "pg_catalog.pg_tables",
        "pg_catalog.pg_class",
        "pg_catalog.pg_attribute",
        "pg_catalog.pg_namespace",
        "pg_catalog.pg_type",
        "pg_catalog.pg_index",
        "pg_catalog.pg_indexes",
        "pg_catalog.pg_constraint",
        "pg_catalog.pg_proc",
        "pg_catalog.pg_trigger",
        "pg_catalog.pg_description",
        "pg_catalog.pg_stat_user_tables",
        "pg_catalog.pg_sequence",
        "pg_catalog.pg_attrdef",
        "pg_catalog.pg_depend",
        "pg_catalog.pg_am",
        "pg_catalog.pg_roles",
        "pg_catalog.pg_user",
        "pg_tables",
        "pg_class",
        "pg_attribute",
        "pg_namespace",
        "pg_type",
        "pg_index",
        "pg_indexes",
        "pg_constraint",
        "pg_proc",
        "pg_trigger",
        "pg_description",
        "pg_stat_user_tables",
        "pg_sequence",
        "pg_roles",
        "pg_user",
    };

    // Scalar function / expression patterns
    private static readonly Regex CurrentDatabasePattern =
        new(@"\bcurrent_database\s*\(\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex VersionPattern =
        new(@"\bversion\s*\(\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CurrentUserPattern =
        new(@"\bcurrent_user\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CurrentSchemaPattern =
        new(@"\bcurrent_schema\s*\(\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SessionUserPattern =
        new(@"\bsession_user\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PgPostmasterStartTimePattern =
        new(@"\bpg_postmaster_start_time\s*\(\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // WHERE clause extraction helpers
    private static readonly Regex TableNameWherePattern =
        new(@"table_name\s*=\s*'([^']+)'", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TableSchemaWherePattern =
        new(@"table_schema\s*=\s*'([^']+)'", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SchemaNameWherePattern =
        new(@"schemaname\s*=\s*'([^']+)'", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Tries to intercept a SQL query and produce catalog result rows.
    /// Returns true if the query was handled; false if it should be forwarded to the engine.
    /// </summary>
    /// <param name="sql">The raw SQL query text.</param>
    /// <param name="database">The live database instance for schema introspection.</param>
    /// <param name="databaseName">The active database name.</param>
    /// <param name="userName">The authenticated user name.</param>
    /// <param name="rows">Output: result rows.</param>
    /// <param name="columns">Output: ordered column names.</param>
    /// <returns>True if catalog query was handled.</returns>
    public bool TryHandleCatalogQuery(
        string sql,
        IDatabase database,
        string databaseName,
        string userName,
        out List<Dictionary<string, object?>> rows,
        out List<string> columns)
    {
        var normalized = NormalizeSql(sql);

        // Scalar-only queries (no FROM clause)
        if (TryHandleScalarQuery(normalized, databaseName, userName, out rows, out columns))
        {
            logger.LogDebug("Catalog: handled scalar query for '{Sql}'", TruncateSql(sql));
            return true;
        }

        // Detect catalog source in FROM clause
        var source = DetectCatalogSource(normalized);
        if (source is null)
        {
            rows = [];
            columns = [];
            return false;
        }

        logger.LogDebug("Catalog: intercepting query on '{Source}'", source);

        var tables = database.GetTables();
        var handled = source switch
        {
            var s when s.EndsWith("information_schema.tables", StringComparison.OrdinalIgnoreCase)
                => HandleInformationSchemaTables(normalized, tables, databaseName, out rows, out columns),

            var s when s.EndsWith("information_schema.columns", StringComparison.OrdinalIgnoreCase)
                => HandleInformationSchemaColumns(normalized, tables, database, databaseName, out rows, out columns),

            var s when s.EndsWith("information_schema.schemata", StringComparison.OrdinalIgnoreCase)
                => HandleInformationSchemaSchemata(databaseName, out rows, out columns),

            var s when s.EndsWith("information_schema.table_constraints", StringComparison.OrdinalIgnoreCase)
                    || s.EndsWith("information_schema.key_column_usage", StringComparison.OrdinalIgnoreCase)
                    || s.EndsWith("information_schema.referential_constraints", StringComparison.OrdinalIgnoreCase)
                    || s.EndsWith("information_schema.constraint_column_usage", StringComparison.OrdinalIgnoreCase)
                => HandleEmptyCatalog(ConstraintColumns, out rows, out columns),

            var s when s.EndsWith("pg_tables", StringComparison.OrdinalIgnoreCase)
                => HandlePgTables(normalized, tables, out rows, out columns),

            var s when s.EndsWith("pg_class", StringComparison.OrdinalIgnoreCase)
                => HandlePgClass(tables, out rows, out columns),

            var s when s.EndsWith("pg_attribute", StringComparison.OrdinalIgnoreCase)
                => HandlePgAttribute(tables, database, out rows, out columns),

            var s when s.EndsWith("pg_namespace", StringComparison.OrdinalIgnoreCase)
                => HandlePgNamespace(out rows, out columns),

            var s when s.EndsWith("pg_type", StringComparison.OrdinalIgnoreCase)
                => HandlePgType(out rows, out columns),

            var s when s.EndsWith("pg_roles", StringComparison.OrdinalIgnoreCase)
                    || s.EndsWith("pg_user", StringComparison.OrdinalIgnoreCase)
                => HandlePgRoles(userName, out rows, out columns),

            var s when s.EndsWith("pg_am", StringComparison.OrdinalIgnoreCase)
                => HandlePgAm(out rows, out columns),

            var s when s.EndsWith("pg_indexes", StringComparison.OrdinalIgnoreCase)
                => HandleEmptyCatalog(PgIndexesColumns, out rows, out columns),

            var s when s.EndsWith("pg_index", StringComparison.OrdinalIgnoreCase)
                => HandleEmptyCatalog(PgIndexColumns, out rows, out columns),

            var s when s.EndsWith("pg_constraint", StringComparison.OrdinalIgnoreCase)
                => HandleEmptyCatalog(PgConstraintColumns, out rows, out columns),

            var s when s.EndsWith("pg_proc", StringComparison.OrdinalIgnoreCase)
                => HandleEmptyCatalog(PgProcColumns, out rows, out columns),

            var s when s.EndsWith("pg_trigger", StringComparison.OrdinalIgnoreCase)
                => HandleEmptyCatalog(PgTriggerColumns, out rows, out columns),

            var s when s.EndsWith("pg_description", StringComparison.OrdinalIgnoreCase)
                => HandleEmptyCatalog(PgDescriptionColumns, out rows, out columns),

            var s when s.EndsWith("pg_stat_user_tables", StringComparison.OrdinalIgnoreCase)
                => HandleEmptyCatalog(PgStatUserTablesColumns, out rows, out columns),

            var s when s.EndsWith("pg_sequence", StringComparison.OrdinalIgnoreCase)
                => HandleEmptyCatalog(PgSequenceColumns, out rows, out columns),

            var s when s.EndsWith("pg_attrdef", StringComparison.OrdinalIgnoreCase)
                => HandleEmptyCatalog(PgAttrDefColumns, out rows, out columns),

            var s when s.EndsWith("pg_depend", StringComparison.OrdinalIgnoreCase)
                => HandleEmptyCatalog(PgDependColumns, out rows, out columns),

            _ => HandleEmptyCatalog(GenericCatalogColumns, out rows, out columns),
        };

        return handled;
    }

    // --------------- scalar query handlers ---------------

    private static bool TryHandleScalarQuery(
        string normalized,
        string databaseName,
        string userName,
        out List<Dictionary<string, object?>> rows,
        out List<string> columns)
    {
        // Only intercept pure scalar queries (SELECT ... without FROM pointing at real tables)
        if (!normalized.Contains("select ", StringComparison.OrdinalIgnoreCase))
        {
            rows = [];
            columns = [];
            return false;
        }

        // Check if any known catalog source is referenced — if so, not a scalar query
        if (DetectCatalogSource(normalized) is not null)
        {
            rows = [];
            columns = [];
            return false;
        }

        var hasCurrentDb = CurrentDatabasePattern.IsMatch(normalized);
        var hasVersion = VersionPattern.IsMatch(normalized);
        var hasCurrentUser = CurrentUserPattern.IsMatch(normalized)
                          || SessionUserPattern.IsMatch(normalized);
        var hasCurrentSchema = CurrentSchemaPattern.IsMatch(normalized);
        var hasPostmasterTime = PgPostmasterStartTimePattern.IsMatch(normalized);

        // Must match at least one scalar function we know
        if (!hasCurrentDb && !hasVersion && !hasCurrentUser && !hasCurrentSchema && !hasPostmasterTime)
        {
            rows = [];
            columns = [];
            return false;
        }

        columns = [];
        Dictionary<string, object?> row = [];

        if (hasCurrentDb)
        {
            columns.Add("current_database");
            row["current_database"] = databaseName;
        }

        if (hasVersion)
        {
            columns.Add("version");
            row["version"] = ServerVersion;
        }

        if (hasCurrentUser)
        {
            columns.Add("current_user");
            row["current_user"] = userName;
        }

        if (hasCurrentSchema)
        {
            columns.Add("current_schema");
            row["current_schema"] = "public";
        }

        if (hasPostmasterTime)
        {
            columns.Add("pg_postmaster_start_time");
            row["pg_postmaster_start_time"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ssZ");
        }

        rows = [row];
        return true;
    }

    // --------------- information_schema handlers ---------------

    private static bool HandleInformationSchemaTables(
        string sql,
        IReadOnlyList<TableInfo> tables,
        string databaseName,
        out List<Dictionary<string, object?>> rows,
        out List<string> columns)
    {
        columns =
        [
            "table_catalog", "table_schema", "table_name", "table_type",
            "self_referencing_column_name", "reference_generation",
            "user_defined_type_catalog", "user_defined_type_schema", "user_defined_type_name",
            "is_insertable_into", "is_typed", "commit_action"
        ];

        // Filter by schema if requested — we only have 'public'
        var schemaFilter = ExtractTableSchemaFilter(sql);
        if (schemaFilter is not null && !string.Equals(schemaFilter, "public", StringComparison.OrdinalIgnoreCase))
        {
            rows = [];
            return true;
        }

        rows = tables.Select(t => new Dictionary<string, object?>
        {
            ["table_catalog"] = databaseName,
            ["table_schema"] = "public",
            ["table_name"] = t.Name,
            ["table_type"] = "BASE TABLE",
            ["self_referencing_column_name"] = null,
            ["reference_generation"] = null,
            ["user_defined_type_catalog"] = null,
            ["user_defined_type_schema"] = null,
            ["user_defined_type_name"] = null,
            ["is_insertable_into"] = "YES",
            ["is_typed"] = "NO",
            ["commit_action"] = null,
        }).ToList();

        return true;
    }

    private static bool HandleInformationSchemaColumns(
        string sql,
        IReadOnlyList<TableInfo> tables,
        IDatabase database,
        string databaseName,
        out List<Dictionary<string, object?>> rows,
        out List<string> columns)
    {
        columns =
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

        var tableFilter = ExtractTableNameFilter(sql);
        var schemaFilter = ExtractTableSchemaFilter(sql);

        if (schemaFilter is not null && !string.Equals(schemaFilter, "public", StringComparison.OrdinalIgnoreCase))
        {
            rows = [];
            return true;
        }

        rows = [];
        foreach (var tableInfo in tables)
        {
            if (tableFilter is not null && !string.Equals(tableFilter, tableInfo.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            var cols = database.GetColumns(tableInfo.Name);
            foreach (var col in cols)
            {
                rows.Add(new Dictionary<string, object?>
                {
                    ["table_catalog"] = databaseName,
                    ["table_schema"] = "public",
                    ["table_name"] = tableInfo.Name,
                    ["column_name"] = col.Name,
                    ["ordinal_position"] = col.Ordinal + 1,
                    ["column_default"] = null,
                    ["is_nullable"] = col.IsNullable ? "YES" : "NO",
                    ["data_type"] = MapToSqlType(col.DataType),
                    ["character_maximum_length"] = IsStringType(col.DataType) ? (object?)null : null,
                    ["character_octet_length"] = null,
                    ["numeric_precision"] = IsNumericType(col.DataType) ? (object?)null : null,
                    ["numeric_precision_radix"] = null,
                    ["numeric_scale"] = null,
                    ["datetime_precision"] = null,
                    ["interval_type"] = null,
                    ["interval_precision"] = null,
                    ["character_set_catalog"] = null,
                    ["character_set_schema"] = null,
                    ["character_set_name"] = null,
                    ["collation_catalog"] = col.Collation is not null ? databaseName : null,
                    ["collation_schema"] = col.Collation is not null ? "pg_catalog" : null,
                    ["collation_name"] = col.Collation,
                    ["domain_catalog"] = null,
                    ["domain_schema"] = null,
                    ["domain_name"] = null,
                    ["udt_catalog"] = databaseName,
                    ["udt_schema"] = "pg_catalog",
                    ["udt_name"] = MapToUdtName(col.DataType),
                    ["scope_catalog"] = null,
                    ["scope_schema"] = null,
                    ["scope_name"] = null,
                    ["maximum_cardinality"] = null,
                    ["dtd_identifier"] = (col.Ordinal + 1).ToString(),
                    ["is_self_referencing"] = "NO",
                    ["is_identity"] = "NO",
                    ["identity_generation"] = null,
                    ["identity_start"] = null,
                    ["identity_increment"] = null,
                    ["identity_maximum"] = null,
                    ["identity_minimum"] = null,
                    ["identity_cycle"] = "NO",
                    ["is_generated"] = "NEVER",
                    ["generation_expression"] = null,
                    ["is_updatable"] = "YES",
                });
            }
        }

        return true;
    }

    private static bool HandleInformationSchemaSchemata(
        string databaseName,
        out List<Dictionary<string, object?>> rows,
        out List<string> columns)
    {
        columns = ["catalog_name", "schema_name", "schema_owner", "default_character_set_catalog",
                   "default_character_set_schema", "default_character_set_name", "sql_path"];

        rows =
        [
            new()
            {
                ["catalog_name"] = databaseName,
                ["schema_name"] = "public",
                ["schema_owner"] = "admin",
                ["default_character_set_catalog"] = null,
                ["default_character_set_schema"] = null,
                ["default_character_set_name"] = null,
                ["sql_path"] = null,
            },
            new()
            {
                ["catalog_name"] = databaseName,
                ["schema_name"] = "pg_catalog",
                ["schema_owner"] = "admin",
                ["default_character_set_catalog"] = null,
                ["default_character_set_schema"] = null,
                ["default_character_set_name"] = null,
                ["sql_path"] = null,
            },
            new()
            {
                ["catalog_name"] = databaseName,
                ["schema_name"] = "information_schema",
                ["schema_owner"] = "admin",
                ["default_character_set_catalog"] = null,
                ["default_character_set_schema"] = null,
                ["default_character_set_name"] = null,
                ["sql_path"] = null,
            },
        ];

        return true;
    }

    // --------------- pg_catalog handlers ---------------

    private static bool HandlePgTables(
        string sql,
        IReadOnlyList<TableInfo> tables,
        out List<Dictionary<string, object?>> rows,
        out List<string> columns)
    {
        columns = ["schemaname", "tablename", "tableowner", "tablespace", "hasindexes", "hasrules", "hastriggers", "rowsecurity"];

        var schemaFilter = ExtractSchemaNameFilter(sql);
        if (schemaFilter is not null && !string.Equals(schemaFilter, "public", StringComparison.OrdinalIgnoreCase))
        {
            rows = [];
            return true;
        }

        rows = tables.Select(t => new Dictionary<string, object?>
        {
            ["schemaname"] = "public",
            ["tablename"] = t.Name,
            ["tableowner"] = "admin",
            ["tablespace"] = null,
            ["hasindexes"] = "f",
            ["hasrules"] = "f",
            ["hastriggers"] = "f",
            ["rowsecurity"] = "f",
        }).ToList();

        return true;
    }

    private static bool HandlePgClass(
        IReadOnlyList<TableInfo> tables,
        out List<Dictionary<string, object?>> rows,
        out List<string> columns)
    {
        columns =
        [
            "oid", "relname", "relnamespace", "reltype", "reloftype", "relowner",
            "relam", "relfilenode", "reltablespace", "relpages", "reltuples",
            "relallvisible", "reltoastrelid", "relhasindex", "relisshared",
            "relpersistence", "relkind", "relnatts", "relchecks", "relhasrules",
            "relhastriggers", "relhassubclass", "relrowsecurity", "relforcerowsecurity",
            "relispopulated", "relreplident", "relispartition", "relrewrite",
            "relfrozenxid", "relminmxid", "relacl", "reloptions", "relpartbound"
        ];

        rows = tables.Select((t, idx) => new Dictionary<string, object?>
        {
            ["oid"] = (idx + 16384).ToString(),
            ["relname"] = t.Name,
            ["relnamespace"] = "2200",  // public namespace OID
            ["reltype"] = (idx + 16385).ToString(),
            ["reloftype"] = "0",
            ["relowner"] = "10",
            ["relam"] = "0",
            ["relfilenode"] = (idx + 16384).ToString(),
            ["reltablespace"] = "0",
            ["relpages"] = "1",
            ["reltuples"] = "-1",
            ["relallvisible"] = "0",
            ["reltoastrelid"] = "0",
            ["relhasindex"] = "f",
            ["relisshared"] = "f",
            ["relpersistence"] = "p",
            ["relkind"] = "r",  // ordinary table
            ["relnatts"] = "0",
            ["relchecks"] = "0",
            ["relhasrules"] = "f",
            ["relhastriggers"] = "f",
            ["relhassubclass"] = "f",
            ["relrowsecurity"] = "f",
            ["relforcerowsecurity"] = "f",
            ["relispopulated"] = "t",
            ["relreplident"] = "d",
            ["relispartition"] = "f",
            ["relrewrite"] = "0",
            ["relfrozenxid"] = "0",
            ["relminmxid"] = "1",
            ["relacl"] = null,
            ["reloptions"] = null,
            ["relpartbound"] = null,
        }).ToList();

        return true;
    }

    private static bool HandlePgAttribute(
        IReadOnlyList<TableInfo> tables,
        IDatabase database,
        out List<Dictionary<string, object?>> rows,
        out List<string> columns)
    {
        columns =
        [
            "attrelid", "attname", "atttypid", "attstattarget", "attlen",
            "attnum", "attndims", "attcacheoff", "atttypmod", "attbyval",
            "attalign", "attstorage", "attcompression", "attnotnull", "atthasdef",
            "atthasmissing", "attidentity", "attgenerated", "attisdropped",
            "attislocal", "attinhcount", "attcollation", "attacl", "attoptions", "attfdwoptions"
        ];

        rows = [];
        foreach (var (tableInfo, tableIdx) in tables.Select((t, i) => (t, i)))
        {
            var cols = database.GetColumns(tableInfo.Name);
            foreach (var col in cols)
            {
                rows.Add(new Dictionary<string, object?>
                {
                    ["attrelid"] = (tableIdx + 16384).ToString(),
                    ["attname"] = col.Name,
                    ["atttypid"] = MapToTypeOid(col.DataType).ToString(),
                    ["attstattarget"] = "-1",
                    ["attlen"] = "-1",
                    ["attnum"] = (col.Ordinal + 1).ToString(),
                    ["attndims"] = "0",
                    ["attcacheoff"] = "-1",
                    ["atttypmod"] = "-1",
                    ["attbyval"] = "f",
                    ["attalign"] = "i",
                    ["attstorage"] = "x",
                    ["attcompression"] = "",
                    ["attnotnull"] = col.IsNullable ? "f" : "t",
                    ["atthasdef"] = "f",
                    ["atthasmissing"] = "f",
                    ["attidentity"] = "",
                    ["attgenerated"] = "",
                    ["attisdropped"] = "f",
                    ["attislocal"] = "t",
                    ["attinhcount"] = "0",
                    ["attcollation"] = col.Collation is not null ? "100" : "0",
                    ["attacl"] = null,
                    ["attoptions"] = null,
                    ["attfdwoptions"] = null,
                });
            }
        }

        return true;
    }

    private static bool HandlePgNamespace(
        out List<Dictionary<string, object?>> rows,
        out List<string> columns)
    {
        columns = ["oid", "nspname", "nspowner", "nspacl"];

        rows =
        [
            new() { ["oid"] = "2200",  ["nspname"] = "public",             ["nspowner"] = "10", ["nspacl"] = null },
            new() { ["oid"] = "11",    ["nspname"] = "pg_catalog",          ["nspowner"] = "10", ["nspacl"] = null },
            new() { ["oid"] = "12387", ["nspname"] = "information_schema",  ["nspowner"] = "10", ["nspacl"] = null },
        ];

        return true;
    }

    private static bool HandlePgType(
        out List<Dictionary<string, object?>> rows,
        out List<string> columns)
    {
        columns = ["oid", "typname", "typnamespace", "typowner", "typlen", "typbyval",
                   "typtype", "typcategory", "typispreferred", "typisdefined",
                   "typdelim", "typrelid", "typsubscript", "typelem", "typarray",
                   "typinput", "typoutput", "typreceive", "typsend", "typmodin",
                   "typmodout", "typanalyze", "typalign", "typstorage",
                   "typnotnull", "typbasetype", "typtypmod", "typndims", "typcollation",
                   "typdefaultbin", "typdefault", "typacl"];

        // Return the most common base types
        rows =
        [
            PgTypeRow("25",   "text",      "s", "S"),
            PgTypeRow("23",   "int4",      "b", "N"),
            PgTypeRow("20",   "int8",      "b", "N"),
            PgTypeRow("701",  "float8",    "b", "N"),
            PgTypeRow("16",   "bool",      "b", "B"),
            PgTypeRow("1114", "timestamp", "b", "D"),
            PgTypeRow("1184", "timestamptz","b","D"),
            PgTypeRow("1082", "date",      "b", "D"),
            PgTypeRow("2950", "uuid",      "b", "U"),
            PgTypeRow("17",   "bytea",     "b", "U"),
            PgTypeRow("1700", "numeric",   "b", "N"),
            PgTypeRow("114",  "json",      "b", "U"),
            PgTypeRow("3802", "jsonb",     "b", "U"),
        ];

        return true;
    }

    private static bool HandlePgRoles(
        string userName,
        out List<Dictionary<string, object?>> rows,
        out List<string> columns)
    {
        columns = ["oid", "rolname", "rolsuper", "rolinherit", "rolcreaterole", "rolcreatedb",
                   "rolcanlogin", "rolreplication", "rolconnlimit", "rolpassword", "rolvaliduntil",
                   "rolbypassrls", "rolconfig", "oid"];

        rows =
        [
            new()
            {
                ["oid"] = "10",
                ["rolname"] = userName,
                ["rolsuper"] = "t",
                ["rolinherit"] = "t",
                ["rolcreaterole"] = "t",
                ["rolcreatedb"] = "t",
                ["rolcanlogin"] = "t",
                ["rolreplication"] = "f",
                ["rolconnlimit"] = "-1",
                ["rolpassword"] = "********",
                ["rolvaliduntil"] = null,
                ["rolbypassrls"] = "f",
                ["rolconfig"] = null,
            }
        ];

        return true;
    }

    private static bool HandlePgAm(
        out List<Dictionary<string, object?>> rows,
        out List<string> columns)
    {
        columns = ["oid", "amname", "amhandler", "amtype"];

        rows =
        [
            new() { ["oid"] = "403", ["amname"] = "btree",  ["amhandler"] = "bthandler",  ["amtype"] = "i" },
            new() { ["oid"] = "405", ["amname"] = "hash",   ["amhandler"] = "hashhandler", ["amtype"] = "i" },
        ];

        return true;
    }

    private static bool HandleEmptyCatalog(
        IReadOnlyList<string> columnNames,
        out List<Dictionary<string, object?>> rows,
        out List<string> columns)
    {
        columns = columnNames.ToList();
        rows = [];
        return true;
    }

    // --------------- helpers ---------------

    private static string NormalizeSql(string sql)
        => sql.Trim().ToLowerInvariant();

    private static string TruncateSql(string sql)
        => sql.Length > 80 ? sql[..80] + "…" : sql;

    private static string? DetectCatalogSource(string normalized)
    {
        var matched = KnownCatalogSources
            .Where(source => normalized.Contains(source, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(source => source.Length)
            .FirstOrDefault();

        return matched;
    }

    private static string? ExtractTableNameFilter(string sql)
    {
        var m = TableNameWherePattern.Match(sql);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string? ExtractTableSchemaFilter(string sql)
    {
        var m = TableSchemaWherePattern.Match(sql);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string? ExtractSchemaNameFilter(string sql)
    {
        var m = SchemaNameWherePattern.Match(sql);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string MapToSqlType(string? dataType) => (dataType?.ToUpperInvariant()) switch
    {
        "INTEGER" or "INT" or "INT4" or "INT32" => "integer",
        "BIGINT" or "INT8" or "INT64" => "bigint",
        "SMALLINT" or "INT2" or "INT16" => "smallint",
        "FLOAT" or "REAL" or "FLOAT4" => "real",
        "DOUBLE" or "FLOAT8" or "DOUBLE PRECISION" => "double precision",
        "DECIMAL" or "NUMERIC" => "numeric",
        "BOOLEAN" or "BOOL" => "boolean",
        "TEXT" or "STRING" or "VARCHAR" or "NVARCHAR" or "CHAR" => "text",
        "BLOB" or "BYTEA" or "BINARY" => "bytea",
        "TIMESTAMP" or "DATETIME" => "timestamp without time zone",
        "TIMESTAMPTZ" => "timestamp with time zone",
        "DATE" => "date",
        "TIME" => "time without time zone",
        "UUID" or "GUID" => "uuid",
        "JSON" => "json",
        "JSONB" => "jsonb",
        _ => "text",
    };

    private static string MapToUdtName(string? dataType) => (dataType?.ToUpperInvariant()) switch
    {
        "INTEGER" or "INT" or "INT4" or "INT32" => "int4",
        "BIGINT" or "INT8" or "INT64" => "int8",
        "SMALLINT" or "INT2" or "INT16" => "int2",
        "FLOAT" or "REAL" or "FLOAT4" => "float4",
        "DOUBLE" or "FLOAT8" or "DOUBLE PRECISION" => "float8",
        "DECIMAL" or "NUMERIC" => "numeric",
        "BOOLEAN" or "BOOL" => "bool",
        "BLOB" or "BYTEA" or "BINARY" => "bytea",
        "TIMESTAMP" or "DATETIME" => "timestamp",
        "TIMESTAMPTZ" => "timestamptz",
        "DATE" => "date",
        "TIME" => "time",
        "UUID" or "GUID" => "uuid",
        "JSON" => "json",
        "JSONB" => "jsonb",
        _ => "text",
    };

    private static int MapToTypeOid(string? dataType) => (dataType?.ToUpperInvariant()) switch
    {
        "INTEGER" or "INT" or "INT4" or "INT32" => 23,
        "BIGINT" or "INT8" or "INT64" => 20,
        "SMALLINT" or "INT2" or "INT16" => 21,
        "FLOAT" or "REAL" or "FLOAT4" => 700,
        "DOUBLE" or "FLOAT8" or "DOUBLE PRECISION" => 701,
        "DECIMAL" or "NUMERIC" => 1700,
        "BOOLEAN" or "BOOL" => 16,
        "BLOB" or "BYTEA" or "BINARY" => 17,
        "TIMESTAMP" or "DATETIME" => 1114,
        "TIMESTAMPTZ" => 1184,
        "DATE" => 1082,
        "TIME" => 1083,
        "UUID" or "GUID" => 2950,
        "JSON" => 114,
        "JSONB" => 3802,
        _ => 25, // text
    };

    private static bool IsStringType(string? dataType) => (dataType?.ToUpperInvariant()) switch
    {
        "TEXT" or "STRING" or "VARCHAR" or "NVARCHAR" or "CHAR" => true,
        _ => false,
    };

    private static bool IsNumericType(string? dataType) => (dataType?.ToUpperInvariant()) switch
    {
        "INTEGER" or "INT" or "INT4" or "INT32"
            or "BIGINT" or "INT8" or "INT64"
            or "SMALLINT" or "INT2" or "INT16"
            or "FLOAT" or "REAL" or "FLOAT4"
            or "DOUBLE" or "FLOAT8"
            or "DECIMAL" or "NUMERIC" => true,
        _ => false,
    };

    private static Dictionary<string, object?> PgTypeRow(string oid, string name, string typtype, string typcat)
        => new()
        {
            ["oid"] = oid,
            ["typname"] = name,
            ["typnamespace"] = "11",
            ["typowner"] = "10",
            ["typlen"] = "-1",
            ["typbyval"] = "f",
            ["typtype"] = typtype,
            ["typcategory"] = typcat,
            ["typispreferred"] = "f",
            ["typisdefined"] = "t",
            ["typdelim"] = ",",
            ["typrelid"] = "0",
            ["typsubscript"] = "-",
            ["typelem"] = "0",
            ["typarray"] = "0",
            ["typinput"] = name + "in",
            ["typoutput"] = name + "out",
            ["typreceive"] = name + "recv",
            ["typsend"] = name + "send",
            ["typmodin"] = "-",
            ["typmodout"] = "-",
            ["typanalyze"] = "-",
            ["typalign"] = "i",
            ["typstorage"] = "x",
            ["typnotnull"] = "f",
            ["typbasetype"] = "0",
            ["typtypmod"] = "-1",
            ["typndims"] = "0",
            ["typcollation"] = "0",
            ["typdefaultbin"] = null,
            ["typdefault"] = null,
            ["typacl"] = null,
        };

    // Column list for empty catalog result sets
    private static readonly string[] ConstraintColumns =
    [
        "constraint_catalog", "constraint_schema", "constraint_name",
        "table_catalog", "table_schema", "table_name", "constraint_type",
        "is_deferrable", "initially_deferred", "enforced"
    ];

    private static readonly string[] PgIndexesColumns =
    [
        "schemaname", "tablename", "indexname", "tablespace", "indexdef"
    ];

    private static readonly string[] PgIndexColumns =
    [
        "indexrelid", "indrelid", "indnatts", "indnkeyatts", "indisunique", "indnullsnotdistinct",
        "indisprimary", "indisexclusion", "indimmediate", "indisclustered", "indisvalid", "indcheckxmin",
        "indisready", "indislive", "indisreplident", "indkey", "indcollation", "indclass", "indoption",
        "indexprs", "indpred"
    ];

    private static readonly string[] PgConstraintColumns =
    [
        "oid", "conname", "connamespace", "contype", "condeferrable", "condeferred", "convalidated",
        "conrelid", "contypid", "conindid", "conparentid", "confrelid", "confupdtype", "confdeltype",
        "confmatchtype", "conislocal", "coninhcount", "connoinherit", "conkey", "confkey", "conpfeqop",
        "conppeqop", "conffeqop", "confdelsetcols", "conexclop", "conbin"
    ];

    private static readonly string[] PgProcColumns =
    [
        "oid", "proname", "pronamespace", "proowner", "prolang", "procost", "prorows", "provariadic",
        "prosupport", "prokind", "prosecdef", "proleakproof", "proisstrict", "proretset", "provolatile",
        "proparallel", "pronargs", "pronargdefaults", "prorettype", "proargtypes", "proallargtypes",
        "proargmodes", "proargnames", "proargdefaults", "protrftypes", "prosrc", "probin", "prosqlbody",
        "proconfig", "proacl"
    ];

    private static readonly string[] PgTriggerColumns =
    [
        "oid", "tgrelid", "tgparentid", "tgname", "tgfoid", "tgtype", "tgenabled", "tgisinternal",
        "tgconstrrelid", "tgconstrindid", "tgconstraint", "tgdeferrable", "tginitdeferred", "tgnargs",
        "tgattr", "tgargs", "tgqual", "tgoldtable", "tgnewtable"
    ];

    private static readonly string[] PgDescriptionColumns =
    [
        "objoid", "classoid", "objsubid", "description"
    ];

    private static readonly string[] PgStatUserTablesColumns =
    [
        "relid", "schemaname", "relname", "seq_scan", "seq_tup_read", "idx_scan", "idx_tup_fetch",
        "n_tup_ins", "n_tup_upd", "n_tup_del", "n_tup_hot_upd", "n_live_tup", "n_dead_tup", "n_mod_since_analyze",
        "n_ins_since_vacuum", "last_vacuum", "last_autovacuum", "last_analyze", "last_autoanalyze",
        "vacuum_count", "autovacuum_count", "analyze_count", "autoanalyze_count"
    ];

    private static readonly string[] PgSequenceColumns =
    [
        "seqrelid", "seqtypid", "seqstart", "seqincrement", "seqmax", "seqmin", "seqcache", "seqcycle"
    ];

    private static readonly string[] PgAttrDefColumns =
    [
        "oid", "adrelid", "adnum", "adbin"
    ];

    private static readonly string[] PgDependColumns =
    [
        "classid", "objid", "objsubid", "refclassid", "refobjid", "refobjsubid", "deptype"
    ];

    private static readonly string[] GenericCatalogColumns = ["oid"];
}


