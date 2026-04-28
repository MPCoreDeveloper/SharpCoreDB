# SharpCoreDB SQL Dialect Extensions (v1.7.2)

This document lists SharpCoreDB-specific SQL dialect features beyond baseline SQL/SQLite compatibility.

## GRAPH_RAG clause

SharpCoreDB supports a top-level `GRAPH_RAG` clause for single-statement graph + vector retrieval.

```sql
SELECT *
FROM documents
GRAPH_RAG 'What are the risks of GDPR in AI?'
  LIMIT 5
  WITH SCORE > 0.85
  WITH CONTEXT
  TOP_K 25;
```

### Supported options

- `LIMIT <n>`
- `WITH SCORE > <threshold>`
- `WITH CONTEXT`
- `TOP_K <n>`

## OPTIONALLY projection mode

SharpCoreDB supports `OPTIONALLY` after the select list to request optional-value mapping.

```sql
SELECT id, name, email OPTIONALLY
FROM users
WHERE email IS SOME;
```

## SOME/NONE predicates

SharpCoreDB adds null-semantic predicates for optional workflows:

- `IS SOME` — value is present (non-null/non-DBNull)
- `IS NONE` — value is missing (null/DBNull)

Examples:

```sql
SELECT id FROM users WHERE email IS SOME;
SELECT id FROM users WHERE email IS NONE;
```

## Dialect capability flags

`ISqlDialect` includes capability flags to detect feature availability:

- `SupportsGraphRagClause`
- `SupportsOptionallyProjection`
- `SupportsSomeNonePredicates`

`SharpCoreDbDialect` enables all three flags in v1.7.2.
