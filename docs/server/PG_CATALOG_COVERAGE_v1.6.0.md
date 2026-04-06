# pg_catalog and information_schema Coverage — V 1.60

**SharpCoreDB Server** intercepts PostgreSQL catalog queries and returns live schema metadata, enabling GUI tools to introspect tables, columns, types, and schemas without requiring native catalog storage.

---

## Architecture

The `PgCatalogService` (`src/SharpCoreDB.Server.Core/Catalog/PgCatalogService.cs`) intercepts catalog queries **before** they reach the database engine:

```
Client SQL  →  BinaryProtocolHandler
                 │
                 ├── PgCatalogService.TryHandleCatalogQuery()
                 │       ↓ matched → synthetic result rows → client
                 │       ↓ not matched → engine.ExecuteQuery()
                 └── SharpCoreDB Engine
```

Both the simple query path (`Q` message) and the extended query path (`P`/`B`/`E` messages) are intercepted.

---

## Supported Scalar Functions

| Expression | Returns |
|---|---|
| `current_database()` | Active database name |
| `version()` | `SharpCoreDB 1.6.0 on .NET 10 (PostgreSQL protocol compatible)` |
| `current_user` | Authenticated user name |
| `session_user` | Authenticated user name |
| `current_schema()` | `public` |
| `pg_postmaster_start_time()` | Server start time (UTC) |

---

## Supported Views

### information_schema

| View | Key Columns | Notes |
|---|---|---|
| `information_schema.tables` | `table_catalog`, `table_schema`, `table_name`, `table_type` | Returns `BASE TABLE` for all user tables in `public` schema. WHERE `table_schema` filter supported. |
| `information_schema.columns` | `table_name`, `column_name`, `ordinal_position`, `data_type`, `is_nullable`, `collation_name` | WHERE `table_name` and `table_schema` filters supported. |
| `information_schema.schemata` | `catalog_name`, `schema_name`, `schema_owner` | Returns `public`, `pg_catalog`, `information_schema`. |
| `information_schema.table_constraints` | All constraint columns | Returns empty set (no constraints stored yet). |
| `information_schema.key_column_usage` | All columns | Returns empty set. |
| `information_schema.referential_constraints` | All columns | Returns empty set. |
| `information_schema.constraint_column_usage` | All columns | Returns empty set. |

### pg_catalog

| View | Key Columns | Notes |
|---|---|---|
| `pg_tables` / `pg_catalog.pg_tables` | `schemaname`, `tablename`, `tableowner` | WHERE `schemaname` filter supported. |
| `pg_class` / `pg_catalog.pg_class` | `oid`, `relname`, `relkind`, `relnamespace` | All user tables as `relkind='r'`. |
| `pg_attribute` / `pg_catalog.pg_attribute` | `attrelid`, `attname`, `atttypid`, `attnum`, `attnotnull` | One row per column across all tables. |
| `pg_namespace` / `pg_catalog.pg_namespace` | `oid`, `nspname`, `nspowner` | Returns `public` (2200), `pg_catalog` (11), `information_schema` (12387). |
| `pg_type` / `pg_catalog.pg_type` | `oid`, `typname`, `typtype`, `typcategory` | 13 base types: text, int4, int8, float8, bool, timestamp, timestamptz, date, uuid, bytea, numeric, json, jsonb. |
| `pg_roles` / `pg_catalog.pg_roles` | `oid`, `rolname`, `rolsuper` | Returns current user as superuser. |
| `pg_user` / `pg_catalog.pg_user` | Same as pg_roles | Alias. |
| `pg_am` / `pg_catalog.pg_am` | `oid`, `amname`, `amtype` | Returns `btree` and `hash`. |
| `pg_index` / `pg_catalog.pg_index` | All index columns | Returns empty set (no index catalog). |
| `pg_indexes` / `pg_catalog.pg_indexes` | All index columns | Returns empty set. |
| `pg_constraint` / `pg_catalog.pg_constraint` | All constraint columns | Returns empty set. |
| `pg_proc` / `pg_catalog.pg_proc` | All proc columns | Returns empty set. |
| `pg_trigger` / `pg_catalog.pg_trigger` | All trigger columns | Returns empty set. |
| `pg_description` / `pg_catalog.pg_description` | All description columns | Returns empty set. |
| `pg_stat_user_tables` | All stat columns | Returns empty set. |
| `pg_sequence` | All sequence columns | Returns empty set. |
| `pg_attrdef` | All attrdef columns | Returns empty set. |
| `pg_depend` | All depend columns | Returns empty set. |

---

## Type Mapping

SharpCoreDB engine types are mapped to standard SQL and PostgreSQL type names:

| Engine Type | SQL Type | pg OID | UDT Name |
|---|---|---|---|
| INTEGER / INT / INT4 / INT32 | integer | 23 | int4 |
| BIGINT / INT8 / INT64 | bigint | 20 | int8 |
| SMALLINT / INT2 / INT16 | smallint | 21 | int2 |
| FLOAT / REAL / FLOAT4 | real | 700 | float4 |
| DOUBLE / FLOAT8 | double precision | 701 | float8 |
| DECIMAL / NUMERIC | numeric | 1700 | numeric |
| BOOLEAN / BOOL | boolean | 16 | bool |
| TEXT / STRING / VARCHAR / NVARCHAR / CHAR | text | 25 | text |
| BLOB / BYTEA / BINARY | bytea | 17 | bytea |
| TIMESTAMP / DATETIME | timestamp without time zone | 1114 | timestamp |
| TIMESTAMPTZ | timestamp with time zone | 1184 | timestamptz |
| DATE | date | 1082 | date |
| UUID / GUID | uuid | 2950 | uuid |
| JSON | json | 114 | json |
| JSONB | jsonb | 3802 | jsonb |

---

## Known Limitations

| Gap | Impact | Workaround |
|---|---|---|
| No index catalog (`pg_index`) | Tools cannot show index details | Partial — tools fall back gracefully |
| No constraint catalog (`pg_constraint`) | PK/FK not shown in GUI | Table structure visible, FK arrows missing |
| No trigger catalog (`pg_trigger`) | Triggers not visible | Not applicable — no trigger support |
| Complex multi-join catalog queries | May not match simple patterns | Tools receive empty rows without error |
| `WHERE` clause parsing | Only equality filters on `table_name`, `table_schema`, `schemaname` | Other filters return all rows |

See `TOOL_COMPATIBILITY_LIMITATIONS_v1.6.0.md` for full gap list and workarounds.

---

## Test Coverage

`tests/SharpCoreDB.Server.IntegrationTests/PgCatalogServiceTests.cs` — 15 tests:

| Test | Validates |
|---|---|
| `TryHandleCatalogQuery_CurrentDatabase_ReturnsDatabaseName` | `current_database()` scalar |
| `TryHandleCatalogQuery_Version_ReturnsVersionString` | `version()` scalar |
| `TryHandleCatalogQuery_CurrentUser_ReturnsUserName` | `current_user` scalar |
| `TryHandleCatalogQuery_InformationSchemaTables_ReturnsUserTables` | Full table list from live schema |
| `TryHandleCatalogQuery_InformationSchemaTables_FilterByPublicSchema_ReturnsRows` | Schema filter pass-through |
| `TryHandleCatalogQuery_InformationSchemaTables_FilterByNonPublicSchema_ReturnsEmpty` | Non-public schema returns empty |
| `TryHandleCatalogQuery_InformationSchemaColumns_ReturnsColumnsForTable` | Column list for specific table |
| `TryHandleCatalogQuery_InformationSchemaColumns_HasOrdinalPosition` | Ordinal position populated |
| `TryHandleCatalogQuery_PgTables_ReturnsUserTables` | `pg_tables` with schema filter |
| `TryHandleCatalogQuery_PgClass_ReturnsRelations` | `pg_class` relkind='r' |
| `TryHandleCatalogQuery_PgNamespace_ContainsPublicSchema` | Three schemas returned |
| `TryHandleCatalogQuery_PgType_ContainsBaseTypes` | Base type catalog |
| `TryHandleCatalogQuery_PgAttribute_ReturnsColumnsForAllTables` | Column attributes |
| `TryHandleCatalogQuery_UserTableQuery_ReturnsFalse` | Non-catalog queries passed through |
| `TryHandleCatalogQuery_InformationSchemaSchemata_ContainsPublic` | Schemata view |

---

*Phase 02 of admin-console roadmap — implemented in SharpCoreDB V 1.60*
