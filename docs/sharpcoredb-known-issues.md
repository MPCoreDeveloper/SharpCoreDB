# SharpCoreDB Known Issues (found while building the ServiceMq.SharpCoreDb provider)

This document describes bugs and limitations found while building the `ServiceMq.SharpCoreDb`
provider against **SharpCoreDB NuGet 1.9.3** (source checked out at
`D:\repos\MPCoreDeveloper\SharpCoreDB`). The items are intended to be picked up in the
SharpCoreDB repository: <https://github.com/MPCoreDeveloper/SharpCoreDB/issues>

---

## 1. 🔴 Table data is NOT encrypted even with `EnableBatchEncryption=true`

> ✅ **RESOLVED (opt-in).** Storage now supports per-record AES-256-GCM at-rest encryption
> behind `DatabaseConfig.EnableAtRestRecordEncryption` (default `false` for full backward
> compatibility — existing databases and `NoEncryptMode` configurations remain byte-for-byte
> unchanged). When enabled, new table data files carry an 8-byte magic header
> (`PersistenceConstants.EncryptedTableMagic`) followed by per-record ciphertext, and all
> read paths (point-reads, full scans, PK index rebuild, compaction) decrypt transparently.
> Legacy plaintext files, empty DDL-created files and `NoEncryptMode=true` presets keep their
> original plaintext layout; mixing is prevented per file. See `Storage.Append.cs`,
> `Storage.ReadWrite.cs`, `IStorage.ReadAllRecords` and `RebuildPrimaryKeyIndexFromDisk`.
>
> **Why opt-in:** the default page-based/columnar engines treat `.dat` as plaintext records;
> silently changing that layout on-disk would break existing databases and older tooling.
> Enable the flag only for new databases whose tables are created/opened with the same flag.
>
> **Migration path:** open the legacy DB (no flag) and rewrite/compact each table so the data
> file is regenerated with the header + ciphertext under the flag, then reopen with the flag.

**Location:** `src/SharpCoreDB/Services/Storage.Append.cs`

- `AppendBytes` / `AppendBytesMultiple` (lines 68-168) always write `data` as plaintext to
  the `.dat` file, prefixed only by a 4-byte length header.
- `FlushBufferedAppends` (lines 175-217) calls `FlushBatch()` when
  `enableBatchEncryption && _batchEncryption.HasPendingData`, but throws the result away
  (comment: *"Note: This is a simplified version"*) and then still writes the plaintext
  appends to disk.
- `BeginBatchEncryption` (line 223) is never called from the normal
  `Database.ExecuteSQL` / `ITable.Insert` paths, so `enableBatchEncryption` has **no effect**
  on the payload files in practice.

**Consequence:** With the default `DatabaseConfig.Default` (`NoEncryptMode=false`) the
`*.dat` table files are still fully readable — the master-password encryption only protects
`meta.dat` (metadata) and the `.salt` file.

**Workaround in ServiceMq.SharpCoreDb:** the provider encrypts every payload itself with
AES-256-GCM (`Protect`/`Unprotect` in `SharpCoreDbMessageStore.cs`). Verified by a test that
scans every file on disk for plaintext (`SharpCoreDbStore_PayloadIsEncryptedAtRest`).

**Recommendation:** In SharpCoreDB the append path should always go through `AesGcmEncryption`
when `!noEncryption`, and `FlushBufferedAppends` should actually write the encrypted result.

---

## 2. 🟠 `Table.Insert` throws `ArgumentOutOfRangeException` after reopening a database

**Location:** `src/SharpCoreDB/Database/Core/Database.Core.cs` → `Load()` (lines 184-428)

- After deserialization, `IsAuto`, `IsNotNull`, `DefaultValues`, and `ColumnCollations` are
  padded to the column count (lines 364-379), but **`DefaultExpressions` and
  `ColumnCheckExpressions` are not**.
- `Table.Insert` (`DataStructures/Table.CRUD.cs`, lines 83-91 and 140-146) indexes
  `this.DefaultExpressions[i]` / `this.ColumnCheckExpressions[i]` without bounds checks →
  `ArgumentOutOfRangeException` when a database is reopened and then an INSERT is performed
  via `ITable.Insert`.

**Workaround in ServiceMq.SharpCoreDb:** `RepairTableSchemaLists()` pads these lists to the
column count.

**Recommendation:** `Database.Load()` should also pad `DefaultExpressions` and
`ColumnCheckExpressions` (exactly like it already does for the other per-column lists).

---

## 3. 🟠 Single-file (.scdb) mode has a no-op B-tree index

**Location:** `src/SharpCoreDB/Interfaces/ITable.cs` (lines 101-107) and
`src/SharpCoreDB/SingleFileTable.cs`

- For single-file tables, `ITable.Index` is a `NullIndex` (no-op): `Search(key)` always
  returns `(false, 0)`.
- `FindByPrimaryKey` on a `SingleFileTable` therefore cannot return what `ITable.Insert`
  wrote via the B-tree — point lookups are unreliable in `.scdb` mode.

**Consequence:** The ServiceMq provider is forced to use directory mode for correct point
operations.

**Recommendation:** Make the single-file PK index functional, or explicitly document that
`FindByPrimaryKey`/`UpdateByPrimaryKey`/`DeleteByPrimaryKey` are not supported in single-file
mode.

---

## 4. 🟡 No read-after-write consistency without an explicit `Flush()`

**Location:** `src/SharpCoreDB/Database/Execution/Database.Execution.cs`

- `Database.ExecuteSQL(...)` only flushes before a SELECT when `_metadataDirty ||
  _batchUpdateActive` (line 137), but `Database.ExecuteQuery(...)` (lines 328-335) does
  **not**.
- After an `ITable.Insert` / `UpdateByPrimaryKey`, changes were invisible to
  `ITable.FindByPrimaryKey` / `ITable.Select()` until an explicit `database.Flush()` occurred.

**Workaround in ServiceMq.SharpCoreDb:** flush before every `SelectArea` scan and on mutations
with `DurabilityMode.FlushToDisk`.

**Recommendation:** `ExecuteQuery` should perform the same dirty-flush check as
`ExecuteSQL(SELECT)`.

---

## 5. 🟡 SQL parameter validation does not match `@`-prefixed keys

**Location:** `src/SharpCoreDB/Services/SqlQueryValidator.cs` (lines 129-153)

- The validator compares parameter keys **without** the `@` prefix against the `@param`
  placeholders in the SQL. Callers using `@`-prefixed keys get false warnings ("Missing
  parameters for placeholders" and "Unused parameters provided").
- `SqlParser.ResolveParameter` trims the `@` itself (`parameterName.TrimStart('@', ':')`),
  so the runtime accepts both — but the validator is inconsistent with it.

**Workaround in ServiceMq.SharpCoreDb:** pass keys without the `@` prefix (helpers strip it
automatically).

**Recommendation:** Let `SqlQueryValidator` account for the `@` prefix in parameter keys so
its behavior is consistent with `SqlParser.ResolveParameter`.

---

## 6. 🟡 `INTEGER` columns map to Int32 instead of Int64

**Location:** `src/SharpCoreDB/Services/SqlParser.ParseValue` and the DDL type mapping

- `INTEGER` → `DataType.Integer` (Int32): values larger than `int.MaxValue` (such as
  `DateTime.Ticks`, ~6.4e17) cause `InvalidOperationException: Value was either too large or
  too small for an Int32`.
- `BIGINT`/`LONG` → `DataType.Long` (Int64).

**Consequence:** `DateTime.UtcNow.Ticks` does not fit in an `INTEGER` column.

**Workaround in ServiceMq.SharpCoreDb:** declare `created_ticks`/`modified_ticks`/`length`
as `BIGINT`.

**Recommendation:** Consider mapping `INTEGER` to Int64 (SQLite behavior), or improve the
error message.

---

## Priority summary

| # | Issue | Severity | Status in ServiceMq.SharpCoreDb |
|---|-------|----------|----------------------------------|
| 1 | Table data not encrypted | 🔴 High | Provider-level AES-256-GCM |
| 2 | `Table.Insert` AOORE after reopen | 🟠 Medium | `RepairTableSchemaLists()` |
| 3 | Single-file no-op index | 🟠 Medium | Directory mode forced |
| 4 | No read-after-write flush | 🟡 Low | Flush calls in provider |
| 5 | SQL validator `@` prefix | 🟡 Low | Keys without `@` prefix |
| 6 | INTEGER → Int32 | 🟡 Low | `BIGINT` columns |