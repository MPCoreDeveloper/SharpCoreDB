# SQLite Compatibility Matrix (SharpCoreDB)

**Status:** Draft — to be completed in Phase 0 (Task 0.6)

## Requirement
SharpCoreDB must be **100% compatible with SQLite syntax and behavior** for all operations users could perform in SQLite. We may extend beyond SQLite, but **must never support less than SQLite**. This requirement applies to the sync provider, schema provisioning, and all generated SQL.

---

## Scope

### DDL (Schema)
| Feature | SQLite Support | SharpCoreDB Support | Notes | Status |
|---|---|---|---|---|
| CREATE TABLE | ✅ | ☐ | | ☐ |
| PRIMARY KEY | ✅ | ☐ | | ☐ |
| UNIQUE | ✅ | ☐ | | ☐ |
| FOREIGN KEY | ✅ | ☐ | | ☐ |
| CREATE INDEX | ✅ | ☐ | | ☐ |
| CREATE TRIGGER | ✅ | ☐ | | ☐ |
| DROP TABLE | ✅ | ☐ | | ☐ |
| DROP TRIGGER | ✅ | ☐ | | ☐ |
| IF EXISTS | ✅ | ☐ | | ☐ |
| DEFAULT values | ✅ | ☐ | | ☐ |
| AUTOINCREMENT | ✅ | ☐ | | ☐ |

### DML (Data)
| Feature | SQLite Support | SharpCoreDB Support | Notes | Status |
|---|---|---|---|---|
| INSERT | ✅ | ☐ | | ☐ |
| INSERT OR REPLACE | ✅ | ☐ | | ☐ |
| UPDATE | ✅ | ☐ | | ☐ |
| DELETE | ✅ | ☐ | | ☐ |
| UPSERT (ON CONFLICT) | ✅ | ☐ | | ☐ |
| WHERE clauses | ✅ | ☐ | | ☐ |
| ORDER BY | ✅ | ☐ | | ☐ |
| GROUP BY | ✅ | ☐ | | ☐ |
| HAVING | ✅ | ☐ | | ☐ |
| GUID/ULID storage | ✅ | ☐ | Validate TEXT/BLOB storage compatibility | ☐ |

### SELECT & JOINs
| Feature | SQLite Support | SharpCoreDB Support | Notes | Status |
|---|---|---|---|---|
| SELECT * | ✅ | ☐ | | ☐ |
| SELECT column list | ✅ | ☐ | | ☐ |
| INNER JOIN | ✅ | ☐ | | ☐ |
| LEFT JOIN | ✅ | ☐ | | ☐ |
| RIGHT JOIN | ✅ | ☐ | | ☐ |
| CROSS JOIN | ✅ | ☐ | | ☐ |
| Subqueries | ✅ | ☐ | | ☐ |
| LIMIT/OFFSET | ✅ | ☐ | | ☐ |

### Functions Used by Sync
| Function | SQLite Support | SharpCoreDB Support | Notes | Status |
|---|---|---|---|---|
| CURRENT_TIMESTAMP | ✅ | ☐ | | ☐ |
| datetime('now') | ✅ | ☐ | | ☐ |
| strftime | ✅ | ☐ | | ☐ |
| last_insert_rowid() | ✅ | ✅ | Already supported | ✅ |
| SYNC_TIMESTAMP() | N/A | ☐ | Custom function | ☐ |

### Trigger Semantics
| Feature | SQLite Support | SharpCoreDB Support | Notes | Status |
|---|---|---|---|---|
| AFTER INSERT | ✅ | ☐ | | ☐ |
| AFTER UPDATE | ✅ | ☐ | | ☐ |
| AFTER DELETE | ✅ | ☐ | | ☐ |
| NEW.* references | ✅ | ☐ | | ☐ |
| OLD.* references | ✅ | ☐ | | ☐ |
| Multiple statements | ✅ | ☐ | | ☐ |

---

## Notes
- This matrix is the authoritative record of SQLite compatibility status.
- Any gaps must be tracked as explicit issues and resolved before Phase 2 completes.
- All sync-generated SQL must stay within SQLite-compatible syntax.
