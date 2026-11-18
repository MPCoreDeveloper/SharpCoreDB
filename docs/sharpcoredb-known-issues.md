# SharpCoreDB Known Issues (found while building the ServiceMq.SharpCoreDb provider)

This document describes bugs and limitations found while building the `ServiceMq.SharpCoreDb`
provider against **SharpCoreDB NuGet 1.9.3** (source checked out at
`D:\repos\MPCoreDeveloper\SharpCoreDB`). The items are intended to be picked up in the
SharpCoreDB repository: <https://github.com/MPCoreDeveloper/SharpCoreDB/issues>

> **Audit status (2026-10-10):** every item below was re-checked against the shipped **2.1.0** tree.
> Five of the six are fixed in the engine (2–5) or resolved by a flip/opt-in (1, 6); each item carries
> a `RESOLVED` / `STILL TRUE` note naming the file that proves it. The report is kept as the historical
> record of a 1.9.3-era integration — an un-annotated sentence inside an item is that item's original
> observation, not a claim about 2.1.0.

---

## 1. 🔴 Table data is NOT encrypted even with `EnableBatchEncryption=true`

> ✅ **RESOLVED (opt-in).** Storage now supports per-record AES-256-GCM at-rest encryption
> behind `DatabaseConfig.EnableAtRestRecordEncryption` — **default `true` since the v2.1 flip
> (2026-09-13)**. This note read "default `false` for full backward compatibility" until
> 2026-09-15, by which point that described no shipped configuration; `NoEncryptMode=true` is the
> documented raw-speed opt-out. The layout is decided **per file**, so existing plaintext
> databases keep theirs and nothing is rewritten behind a caller's back. New table data files
> carry an 8-byte magic header
> (`PersistenceConstants.EncryptedTableMagic`) followed by per-record ciphertext, and all
> read paths (point-reads, full scans, PK index rebuild, compaction) decrypt transparently.
> Legacy plaintext files, empty DDL-created files and `NoEncryptMode=true` presets keep their
> original plaintext layout; mixing is prevented per file. See `Storage.Append.cs`,
> `Storage.ReadWrite.cs`, `IStorage.ReadAllRecords` and `RebuildPrimaryKeyIndexFromDisk`.
>
> **Why it is per file rather than global:** the default page-based/columnar engines used to treat
> `.dat` as plaintext records; rewriting that layout in place would break existing databases and
> older tooling. New files are encrypted by default, existing files keep their layout, and mixing
> is prevented within a file.
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

> ✅ **RESOLVED (verified 2026-10-10, 2.1.0).** `Database.Core.Load()` now pads *all seven* per-column
> lists to `Columns.Count`, including the two this report found short:
> `DefaultExpressions` and `ColumnCheckExpressions` (the `while (… .Count < table.Columns.Count)`
> block in `src/SharpCoreDB/Database/Core/Database.Core.cs`), alongside `IsAuto`, `IsNotNull`,
> `DefaultValues`, `ColumnCollations` and `ColumnLocaleNames`. Reopening a database and then calling
> `ITable.Insert` no longer indexes a short list, so the provider-side `RepairTableSchemaLists()`
> workaround is no longer needed.

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

> ✅ **RESOLVED (verified 2026-10-10, 2.1.0).** `SingleFileTable` now keeps a primary-key hash index
> (`_pkIndex`, marked "Issue A1" in `src/SharpCoreDB/SingleFileTable.cs`) built from the PK column, so
> `FindByPrimaryKey` / `UpdateByPrimaryKey` / `DeleteByPrimaryKey` are O(1) point operations in `.scdb`
> mode — directory mode is no longer required for correct point lookups. The `NullIndex` this report
> saw survives only as the interface's no-op default (`Interfaces/ITable.cs`) for implementations that
> declare no index of their own.

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

> ✅ **RESOLVED (verified 2026-10-10, 2.1.0).** `Database.ExecuteQuery` now flushes pending **batch**
> writes before running the query (`src/SharpCoreDB/Database/Execution/Database.Execution.cs`,
> "FIX (Known Issue 4)"), so rows written through `ITable.Insert` / `UpdateByPrimaryKey` are visible to
> `FindByPrimaryKey` / `Select()` without an explicit `db.Flush()`. One asymmetry is deliberate and
> documented in the code: a plain `_metadataDirty` flag is *not* flushed, because SQL `INSERT`s are
> already visible in-memory on the default engine and a blanket metadata flush there costs more than it
> buys. `IDatabase.Flush()` remains the explicit durability boundary.

**Location:** `src/SharpCoreDB/Database/Execution/Database.Execution.cs`

- `Database.ExecuteSQL(...)` only flushes before a SELECT when `_metadataDirty ||
  _batchUpdateActive` (line 137), but `Database.ExecuteQuery(...)` (lines 328-335) does
  **not**.
- After an `ITable.Insert` / `UpdateByPrimaryKey`, changes were invisible to
  `ITable.FindByPrimaryKey` / `ITable.Select()` until an explicit `database.Flush()` occurred.

**Workaround in ServiceMq.SharpCoreDb:** flush before every `SelectArea` scan and on mutations via
`db.Flush()`. (The historical write-up named `DurabilityMode.FlushToDisk`; that member does not exist —
`DurabilityMode` is `FullSync` | `Async`, and forcing data to physical disk is what `FullSync` and an
explicit `IDatabase.Flush()` perform.)

**Recommendation:** `ExecuteQuery` should perform the same dirty-flush check as
`ExecuteSQL(SELECT)`.

---

## 5. 🟡 SQL parameter validation does not match `@`-prefixed keys

> ✅ **RESOLVED (verified 2026-10-10, 2.1.0).** `SqlQueryValidator` normalises keys before comparing:
> `NormalizeKey` strips a leading `@` or `:` (its comment states "a @-prefixed key and an unprefixed key
> with the same name are equivalent"), so `@name`, `:name` and `name` all satisfy a `@name` placeholder.
> The false "Missing parameters for placeholders" / "Unused parameters provided" warnings are gone.

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

> ⚠️ **STILL TRUE BY DEFAULT, with an opt-in (verified 2026-10-10, 2.1.0).** `INTEGER` →
> `DataType.Integer` (Int32) remains the default, for backward compatibility with existing databases and
> consumer code. The engine now offers the SQLite behaviour explicitly: set
> `DatabaseConfig.UseSqliteIntegerAffinity = true` and `INTEGER` maps to `DataType.Long` (Int64) for
> **newly created or added** columns — existing columns keep the type recorded in their table metadata.
> The option is documented on `DatabaseConfig` in `src/SharpCoreDB/DatabaseConfig.cs`.

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

## 7. ✅ FIXED — A bound string parameter is re-parsed as SQL text, so quotes and backslashes corrupt the row

> ✅ **FIXED (2026-10-26, `master` @ 9461c74e + this change).** Found while building SharpAgent's code-evidence
> index, which writes source text, comments and Windows paths as `TEXT` values. Every reproduction below is now
> a test: `tests/SharpCoreDB.Tests/ParameterLiteralRoundTripTests.cs` (74 cases) failed 32 of 57 before the fix
> and the core suite is **2107 / 0 failed** after it.

### Runnable reproduction

Two files, no test framework, ~40 lines — the whole defect and the whole fix are visible in the console output.
Put them in `artifacts/repro/` (two levels below the repo root, so the `..\..\src` project reference is right) and
run `dotnet run -c Release`.

`Repro.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net11.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
    <ProjectReference Include="..\..\src\SharpCoreDB\SharpCoreDB.csproj" />
  </ItemGroup>

</Project>
```

`Program.cs`

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;

var services = new ServiceCollection();
services.AddSharpCoreDB();
var factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();

var dir = Path.Combine(Path.GetTempPath(), "scdb-issue7-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dir);
var db = factory.Create(dir, "pw", isReadOnly: false, config: new DatabaseConfig { NoEncryptMode = true });
db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, head TEXT, val TEXT, tail TEXT)");

// @val sits in the middle of the tuple, so a value that is split at its own comma moves into `tail`.
string[] middle =
[
    "it's fine, really",
    @"a stray \' apostrophe, then a comma",
    @"ends with a backslash \",
    "plain text",
];

for (int i = 0; i < middle.Length; i++)
{
    Try(i + 1, "INSERT INTO t VALUES (@id, 'head', @val, 'tail')", middle[i]);
}

// The empty string as the *last* value: the tuple ends in ''.
Try(99, "INSERT INTO t VALUES (@id, 'head', 'tail', @val)", string.Empty);

void Try(int id, string sql, string value)
{
    Console.WriteLine($"@val = [{value}]");
    try
    {
        db.ExecuteSQL(sql, new Dictionary<string, object?> { ["id"] = id, ["val"] = value });
        var row = db.ExecuteQuery($"SELECT * FROM t WHERE id = {id}")[0];
        Console.WriteLine($"   val={Show(row["val"])} tail={Show(row["tail"])}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"   {ex.GetType().Name}: {ex.Message}");
    }
}

static string Show(object? value) => value is null or DBNull ? "<NULL>" : $"[{value}]";
```

**Before the fix** — `HEAD` `9461c74e`, the tree this report was filed against (output copied from that run; the
`←` notes are annotations, and `Show` prints `<NULL>` for a null so an empty string and a null cannot be confused):

```text
@val = [it's fine, really]
   val=[its fine] tail=[really, tail]                                    ← apostrophe gone, literal split at its comma
@val = [a stray \' apostrophe, then a comma]
   InvalidOperationException: Mismatched parentheses in VALUES clause
@val = [ends with a backslash \]
   InvalidOperationException: Mismatched parentheses in VALUES clause
@val = [plain text]
   val=[plain text] tail=[tail]
@val = []
   val=[tail] tail=<NULL>                                                ← '' dropped: val overwritten, tail set to NULL
```

**After the fix** — same program, same machine, no other change:

```text
@val = [it's fine, really]
   val=[it's fine, really] tail=[tail]
@val = [a stray \' apostrophe, then a comma]
   val=[a stray \' apostrophe, then a comma] tail=[tail]
@val = [ends with a backslash \]
   val=[ends with a backslash \] tail=[tail]
@val = [plain text]
   val=[plain text] tail=[tail]
@val = []
   val=[tail] tail=[]
```

Every line of that program is also a test case in `tests/SharpCoreDB.Tests/ParameterLiteralRoundTripTests.cs`
(74 cases), so the regression cannot come back silently.

**Fix (what changed).** The formatter and the scanners now share **one** escaping convention, defined once in
`src/SharpCoreDB/Services/SqlLiteralScanner.cs` and used by every literal reader on the statement path:

- **The rule is SQL's, and therefore SQLite's:** inside `'…'` a doubled quote is one quote of data and does not
  close the literal, and a backslash is data. A backslash can never be an escape here — it is a legal last
  character of a value (a Windows path), and a scanner that lets it suppress the closing quote cannot represent
  that value in a statement at all.
- **Converted call sites:** `SqlParser.ParseInsertValues` (now purely structural — it returns each element as
  written and `ParseValue` reads the literal), `SqlParser.FindMatchingCloseParen`,
  `SqlInPredicate.SplitTopLevelComma`, all four `PreparedStatement` extractors, `Database.Batch`
  (`ParseValues`, `ParseValuesToArray`, `ParseValueFast`), `SingleFileDatabase.Batch` (`ParseValueList`,
  `TrimQuotes`), `FastSqlLexer.ReadString`, the `TypeConverter` tokenizer, the `WHERE` tokenizer
  (`Table.Scanning.EvaluateWhere` — splitting on spaces truncated *any* quoted value at its first space),
  `Table.QueryHelpers`, `Table.Collation`, `SqlInPredicate`'s IN/tuple value parsing, `SingleFileTable`,
  `WhereClauseAnalyzer`, `SqlParser.PerformanceOptimizations`, `SqlParserExtensions`, `DatabaseExtensions`, and
  the wire protocol's `$n` → `?` translation (`BinaryProtocolHandler`, which needed
  `InternalsVisibleTo("SharpCoreDB.Server.Core")`).
- **`ParseValue` removes exactly one pair of quotes** and collapses the doubling. The old `while`-strip removed
  every edge quote, so a parameter holding `'` stored an empty string; and it left `''` in place, so `'it''s'`
  reached storage as `it''s`.
- **A quoted empty literal is a value.** `(1, '')` stores an empty string, as SQLite does; only a literally
  absent element is dropped. A *trailing* `''` used to become `NULL`, which made an empty string impossible to
  round-trip.
- **`UPDATE … SET col = @v` splits at the first `=`**, not at every `=`: a value containing `=` (a query string,
  a source line) silently dropped the whole assignment before.
- **Safety net (recommendation 1):** a VALUES tuple that produces more values than the statement can consume now
  throws **before any row is written**, naming the table, the tuple index, both counts and the column list,
  instead of shifting values into the neighbouring column.
- **Two latent test bugs surfaced:** `EncryptionCoverageTests` and `WritePathProfilerTests` interpolated
  `{i * 0.5}` straight into SQL, which renders `0,5` on a comma-decimal machine and is a **four**-value tuple.
  The old parser silently dropped the overflow; the new arity check rejects it (SQLite rejects it too), so those
  two tests now format invariantly.

**Not changed, deliberately:** values are still rendered into the statement text for `ExecuteSQL(sql, params)`.
Recommendation 3 (build the row from the parameter objects and skip the text round trip) remains an
optimisation, not a correctness need now that the convention is shared and pinned.

**Location:** `src/SharpCoreDB/Services/PreparedStatements.cs` (`ParameterBinder.FormatParameter`, lines 478-510),
`src/SharpCoreDB/Services/SqlParser.Core.cs:193` (`Execute(sql, parameters)` → `BindParameters`), and
`src/SharpCoreDB/Services/SqlParser.DML.cs` (`ParseMultiRowInsertValues` 584-628, `FindMatchingCloseParen`
633-661, `ParseInsertValues` 664-823).

- `ExecuteSQL(sql, parameters)` renders every value back **into the statement text**
  (`FormatParameter` returns `'it''s'` for `it's`, `[0.5, 0.25]` for a `float[]`, `NULL` for null) and then
  hands the *rendered* statement to the ordinary parser. The value therefore becomes parser input, and
  correctness depends on the formatter and the literal scanner agreeing exactly.
- They do not agree. The scanner toggles quote state on every `'` whose previously **appended** character is
  not a backslash (`SqlParser.DML.cs:709-717`): standard SQL quote-doubling is not an escape, and there is no
  `toggles == 3` branch, so after a doubled quote the scanner considers itself **outside** the literal for the
  rest of it.

**Reproduced** with `CREATE TABLE t (id INTEGER PRIMARY KEY, a TEXT, b TEXT)` and
`INSERT INTO t VALUES (1, @text, 'tail')`:

| `@text` | What is stored |
|---|---|
| `it's fine, really` | `a = "its fine"`, `b = "really, tail"` — apostrophe dropped, literal split at the comma, **tail shifted into the next column** |
| `a stray \' apostrophe, then a comma` | `InvalidOperationException: Mismatched parentheses in VALUES clause` |
| `ends with a backslash \` | `InvalidOperationException: Mismatched parentheses in VALUES clause` |
| `the mask (see below, the reader) wins, and then it records iterations)` | correct — parens and commas alone are safe, which is why the defect hides |

**Consequence:** a value may land in the **neighbouring column**, including a non-text one. SharpAgent saw it as
`Invalid VECTOR literal '… iterations)'` on `embedding`, and before that as rows rejected with "Mismatched
parentheses". Silent data corruption is the more dangerous half: nothing is thrown for the apostrophe case.

**Why the scanners disagree.** There are four independent quote scanners, each with slightly different rules:

- `SqlParser.ParseInsertValues` / `FindMatchingCloseParen` — backslash-aware, doubling-unaware.
- `SqlInPredicate.SplitTopLevelComma` (`SqlInPredicate.cs:408-456`, the one UPDATE's `SET` list uses) —
  quote/paren/bracket-aware, doubling-unaware, backslash-unaware (`inString = c != quote`).
- `PreparedStatement.ExtractParameterPositions` / `ExtractNamedParameters` (`PreparedStatements.cs:101-194`) —
  backslash-aware, doubling-unaware.
- `ParameterExtractor` — its own copy of the same idea.

And `InsertValuesParsingTests` currently **pins the corruption as intended**:
`DoubledQuote_IsDroppedAndContentConcatenated_NotEscaped` asserts `'it''s'` → `its`, and
`BackslashEscapedQuote_IsKeptVerbatim_AndDoesNotToggle` asserts that a backslash suppresses the toggle.

**Workaround in SharpAgent:** never send free-form text through `ExecuteSQL(sql, parameters)`. Writes go through
`ITable.Insert` / `UpdateByPrimaryKey` with native values (`src/SharpCoreAgent.Indexing/IndexSql.cs`), which
never renders a statement, so no escaping question arises. This is a consumer-side workaround, not a fix: the
same corruption is reachable from any `ExecuteSQL` call with a text parameter.

**Recommendation (in this order):** ✅ 1, 2 and 4 are implemented (see *Fix* above). 3 remains an optional
optimisation. The original analysis is kept below as the record of what was wrong.

1. **Assert the arity.** After `ParseMultiRowInsertValues`, compare `rowValues.Count` with the resolved column
   list and throw naming the column, the literal index and the offset instead of letting a shifted value reach
   `ParseValue`. Converts silent corruption into a loud, diagnosable error, with no hot-path cost.
2. **One scanner, one escaping convention.** Extract a single `SqlLiteralScanner` used by the VALUES splitter,
   `FindMatchingCloseParen`, `SplitTopLevelComma` and both `Extract*` methods, implementing SQL's actual rules
   (`''` inside a literal is one quote and does **not** close it; a backslash is data, not an escape), and make
   `FormatParameter` emit exactly what that scanner reads back — `'` → `''`, and either escape `\` as `\\` or,
   preferably, stop treating `\` as an escape anywhere. Then "any string round-trips byte-exact" becomes a
   testable property rather than a coincidence.
3. **Stop rendering values into statements.** For simple single-table `INSERT … VALUES (…, @p, …)` /
   `UPDATE … SET col = @p WHERE pk = @pk`, build the row dictionary from the parameter objects and call the
   table API the SQL path already ends in (`Table.Insert`, `Table.InsertBatch`, `Table.UpdateByPrimaryKey`).
   No text is produced, so nothing can be mis-escaped, and the statement avoids a format-then-parse round trip.
   The engine already shape-gates a "simple select" plan, so the precedent exists.
4. **Replace the two pinning tests** with round-trip tests (`ExecuteSQL` with a parameter, read back, compare
   byte-exact) over: apostrophes, doubled quotes, backslashes before quotes, trailing backslashes, braces,
   parens, brackets, embedded commas, newlines and non-ASCII. Keep `InsertValuesParsingTests`' literal
   `INSERT … VALUES` cases that assert genuine SQL behaviour, and correct `'it''s'` → `it's`.

**Guardrail for step 2:** the multi-row VALUES scan is a measured hot path (comments at
`SqlParser.DML.cs:572-583` and `:436-450` record the numbers). Any rewrite must keep the slice fast path for
literals without doubling or backslashes, and re-run `--multirowinsert` plus the `WritePathProfiler`
attribution before merge.

---

## 8. ✅ FIXED (directory storage) — An UPDATE that moves a PRIMARY KEY onto an existing key lost a row

> ✅ **FIXED (2026-11-17, `fix/pk-update-and-unique-constraints`) on directory storage.** Every UPDATE path —
> `Table.UpdateAffectedCount` (SQL UPDATE), `UpdateMultipleCore` (`ExecuteBatchSQL` and the structured batch API) and
> `UpdateByPrimaryKey` — now validates a new key before any row is written: a key another row holds, or a SET of the key
> that matches more than one row, is rejected and nothing changes. Pinned by `PrimaryKeyUpdateTests` (columnar and
> page-based, in the session and after a reopen). The single-file half — a duplicated key, not a lost row — is the same
> composite-key question as §11 and is tracked there.

Found 2026-11-17. Neither storage mode checks the new key of an UPDATE that changes the primary key:

```sql
CREATE TABLE t (id INTEGER PRIMARY KEY, email TEXT);
INSERT INTO t (id, email) VALUES (1, 'a@x');
INSERT INTO t (id, email) VALUES (3, 'c@x');
UPDATE t SET id = 1 WHERE id = 3;   -- SQLite: UNIQUE constraint failed: t.id
SELECT id FROM t;
```

- **Directory storage:** the UPDATE is accepted and the original row `1` is **lost** (silent data loss).
- **Single-file storage:** the UPDATE is accepted and two rows carry id `1`.

**Why it is open:** the directory-mode fix belongs in `Table.Update`, the write path the performance gate in
`docs/performance/AUTONOMOUS_AGENT_BRIEF.md` guards; it needs its own change with a gate run.

---

## 9. ✅ FIXED (directory storage) — A column-level `UNIQUE` constraint was not enforced

> ✅ **FIXED (2026-11-17, `fix/pk-update-and-unique-constraints`) on directory storage.** A single-column UNIQUE
> (column-level or table-level `UNIQUE (col)`) gets an automatic unique hash index, as SQLite's `sqlite_autoindex_…` —
> registered at CREATE TABLE and on every open, so existing tables are covered. INSERT, multi-row INSERT (now atomic on
> tables with a unique index) and every UPDATE path are checked; NULLs never conflict. On the way: a UNIQUE *index* was
> enforced only by a single-row INSERT into a columnar table, and a second NULL was rejected as a duplicate. Pinned by
> `UniqueConstraintTests` (columnar and page-based). A multi-column `UNIQUE (a, b)` is enforced too since
> `fix/composite-keys-update-atomicity-orderby` (INSERT, multi-row INSERT and UPDATE; `CompositeKeyTests`). **Still
> open:** single-file storage (its persisted `ColumnFlags.Unique` is also written for every inline PRIMARY KEY column, so
> a reopened file cannot tell a declared UNIQUE from an EF Core composite key's column — the format decision of
> §10/§11; tried and backed out when `CompleteExampleIntegrationTests.CompositeKey_*` failed).

Found 2026-11-17. A column declared `UNIQUE` accepts duplicates on both storage modes:

```sql
CREATE TABLE t (id INTEGER PRIMARY KEY, email TEXT UNIQUE);
INSERT INTO t (id, email) VALUES (1, 'a@x');
INSERT INTO t (id, email) VALUES (2, 'a@x');   -- SQLite: UNIQUE constraint failed: t.email; SharpCoreDB: accepted
```

A UNIQUE *index* is enforced on directory storage; the column constraint is parsed and, on single-file storage,
persisted as `ColumnFlags.Unique`, but no write path checks it. **Why it is open:** same write-path gate as §8.

---

## 10. 🟡 OPEN (owner decision) — Single-file storage ignores a column's declared `COLLATE`

> ⚠️ **STILL TRUE (verified 2026-11-17), pending an owner decision** on the column-entry format extension below.

Found 2026-11-17. On single-file storage `CREATE TABLE t (name TEXT COLLATE NOCASE)` behaves as BINARY: `WHERE
name = 'X'` does not match `'x'`, and `ORDER BY name` sorts case-sensitively. Directory storage honours it.

Three gaps make it up: the single-file `CREATE TABLE` parser (`DatabaseExtensions`) does not read `COLLATE`; the
single-file column entry (`ColumnDefinitionEntry`, 48 bytes) has no collation field; and the single-file WHERE
evaluator (`SingleFileTable.EvaluateSingleCondition`) is static and has no collation support.

**Decision needed:** persisting the collation means claiming spare bits of `ColumnDefinitionEntry.Flags` (bits 8–11
would hold `CollationType`; a file written before reads as 0 = BINARY, today's behaviour). That is compatible with
`docs/manual/upgrade-and-downgrade.md` — old files keep working, downgrade is already unsupported — but it is an
on-disk format extension. A `Locale(…)` collation would still need a name field. Pinned by the skipped
`SingleTableOrderByTests.OrderBy_ColumnDeclaredNoCase_OnSingleFile_SortsCaseInsensitively`.

---

## 11. 🔴 OPEN (owner decision) — PRIMARY KEY uniqueness and composite keys

> ⚠️ **STILL TRUE for single-file storage (verified 2026-11-17), pending an owner decision** on modelling composite
> keys there. Single-file enforcement was implemented and backed out because of the compatibility hazard below.
> **Directory storage is fixed for tables created from `fix/composite-keys-update-atomicity-orderby` on:** a composite
> key — table-level `PRIMARY KEY (a, b)`, `CONSTRAINT name PRIMARY KEY (a, b)`, or `PRIMARY KEY` written on several
> columns as EF Core's `EnsureCreated` writes it — is enforced as SQLite enforces it in a rowid table: a UNIQUE
> constraint over all its columns, the row id staying the storage key (`CompositeKeyTests`). A directory table created
> earlier keeps the key it was created with (its last key column; the other columns were never recorded) and needs to
> be re-created to drop the false violations. The table-level form was worse than described below: it was read as a
> column called `PRIMARY` (or `CONSTRAINT`) that became the key, always NULL, so such a table held one row.

Found 2026-11-17 while fixing single-file constraint enforcement.

- **Single-file storage does not enforce PRIMARY KEY uniqueness.** `INSERT … VALUES (2, 'y'), (1, 'dup')` into a
  table already holding id 1 leaves two rows with id 1, and an UPDATE that moves a key onto an existing one (§8's
  single-file half) duplicates it.
- **Neither mode models a composite key.** EF Core's `EnsureCreated` (`SharpCoreDBDatabaseCreator`) declares one
  inline `PRIMARY KEY` per key column; both storage modes keep only the *last* such column as `PrimaryKeyIndex`.
  Directory storage then enforces uniqueness on that one column — a false violation as soon as two rows share it
  (order lines numbered 1, 2, … per order) — and single-file storage enforces nothing. SQLite rejects the
  declaration outright ("table has more than one primary key").

**Why single-file enforcement was not simply switched on:** its format persists `ColumnFlags.PrimaryKey` on that one
column only, so a file cannot tell a single-column key from a composite key that lost its other columns. Enforcing
single-column uniqueness would make existing EF Core single-file databases with composite keys reject legitimate
inserts after an upgrade — exactly what `CompleteExampleIntegrationTests.CompositeKey_*` showed when it was tried.

**Decision needed:** model composite keys (persist the `PrimaryKey` flag on every key column and enforce uniqueness
on the tuple — for files written before, there is no way to recover the lost key columns, so they would stay
unenforced), and only then enforce single-column uniqueness on single-file storage. Pinned by the skipped
single-file cases in `PrimaryKeyUniquenessTests`.

## 12. ✅ FIXED — Inside a transaction, a columnar full scan did not see the transaction's own UPDATEs and DELETEs

> ✅ **FIXED (2026-11-17, `fix/transaction-rollback-and-scan-overlay`).** The scan's data source
> (`Storage.ReadBytesWithRecordOffsets`) lays the transaction's buffered in-place overwrites and commit-time tombstones
> over the records it returns — only while a transaction holds such changes for the file, so other scans are untouched
> — and a point read (`ReadBytesFrom`) of a record the transaction deleted returns nothing. Encrypted and
> `NoEncryptMode` files alike. Pinned by `InTransactionReadTests`. The description below is kept as the record.

Inside `BeginBatchUpdate()` / `EndBatchUpdate()` — the transaction an ADO.NET `DbTransaction` opens, and so linq2db's
`TransactionAsync` and EF Core's transactions — a columnar table answers reads inconsistently until the commit:

| Read after `UPDATE t SET n = 50 WHERE id = 1` (then `DELETE FROM t WHERE id = 1`) | Columnar | PageBased |
|---|---|---|
| `SELECT n FROM t WHERE id = 1` (primary-key lookup) | 50 (then still 50) | 50 (then none) |
| `SELECT id, n FROM t` (full scan) | 1 — the old value (then still listed) | 50 (then gone) |
| `SELECT id FROM t WHERE n = 50` (scan with a filter) | no row | the row |

**Cause:** the columnar full scan reads the data file through `Storage.ReadBytesWithRecordOffsets`, which returns what
is on disk; the transaction's in-place overwrites and its deletes are held in the storage layer's buffers until the
commit, and only the primary-key path (`engine.Read`) consults them. INSERTs are visible because appends are placed
where the scan finds them. After the commit every read is correct (`BatchUpdateTransactionTests`).

**Fix outline:** overlay the transaction's buffered overwrites and deletes on the scanned records (keyed by physical
record offset, which the scan already carries for its stale-version filter). It is a read-path change on the hottest
scan, so it needs the write-path gate and a read benchmark; not attempted in this change.

## 13. ✅ FIXED — PageBased storage could not roll a transaction back

> ✅ **FIXED (2026-11-17, `fix/transaction-rollback-and-scan-overlay`) for `BeginBatchUpdate` / `CancelBatchUpdate` and
> the storage-only transaction the EF Core provider uses** — so ADO.NET, linq2db and EF Core rollbacks. Each
> `PageManager` keeps a page's image on its first change inside the transaction (`BeginUndoScope`); rollback restores
> the images (marked dirty, so a page already written to the file during the transaction is written back) and returns
> pages the transaction allocated to the free list, then every table rebuilds its primary-key index from storage and
> marks its hash indexes stale — which also fixes a columnar rollback leaving the hash index on the rolled-back value.
> Outside a transaction a write pays one null check. Pinned by `TransactionRollbackTests` (including an ADO.NET
> `DbTransaction.Rollback()`). **Still open:** `ExecuteBatchSQL` rolls its own internal transaction back on a failing
> statement without the undo scope, so a PageBased table keeps the statements of a failed batch that ran before the
> failure; hooking it adds a page copy per touched page to the most-measured write path and needs its own gate run.
> And without a page WAL, a crash in the middle of a transaction can still leave pages the cache wrote early.

`CancelBatchUpdate()` (an ADO.NET `DbTransaction.Rollback()`) does not undo a PageBased table's changes: a rolled-back
UPDATE or INSERT is still there — also after a reopen — and a rolled-back DELETE stays deleted. Columnar tables roll
back correctly (`BatchUpdateTransactionTests.CancelBatchUpdate_Columnar_DiscardsTheUpdate`).

**Cause:** the PageBased engine writes into its page cache and page files directly, outside the storage transaction
buffer, and `PageBasedEngine.Rollback()` is a stub ("For simplicity, we don't support rollback in this POC — a
production implementation would use WAL or shadow paging"). `Database.CancelBatchUpdate` rolls back only the storage
buffer and removes rows it recorded as inserted.

**Fix outline:** an undo image per page touched inside the transaction (copy the page on its first write, restore the
copies and drop the cache entries on rollback), with dirty pages withheld from disk until commit. That is a change to
the PageBased engine's durability path and needs the reopen round-trip matrix and the write-path gate; not attempted
in this change.

---

## Priority summary

| # | Issue | Severity | Status in ServiceMq.SharpCoreDb | In SharpCoreDB 2.1.0 |
|---|-------|----------|----------------------------------|----------------------|
| 1 | Table data not encrypted | 🔴 High | Provider-level AES-256-GCM | **Fixed** — per-record AES-256-GCM on by default (`EnableAtRestRecordEncryption`), flippable since 2026-09-13 |
| 2 | `Table.Insert` AOORE after reopen | 🟠 Medium | `RepairTableSchemaLists()` | **Fixed** — `Load()` pads every per-column list |
| 3 | Single-file no-op index | 🟠 Medium | Directory mode forced | **Fixed** — `SingleFileTable` keeps a PK hash index |
| 4 | No read-after-write flush | 🟡 Low | Flush calls in provider | **Fixed** — `ExecuteQuery` flushes pending batch writes |
| 5 | SQL validator `@` prefix | 🟡 Low | Keys without `@` prefix | **Fixed** — `NormalizeKey` strips `@` / `:` |
| 6 | INTEGER → Int32 | 🟡 Low | `BIGINT` columns | **Still the default** — opt in with `UseSqliteIntegerAffinity` |
| 7 | Bound text re-parsed as SQL | 🔴 High | Write through `ITable.Insert` instead of SQL text | **Fixed** — one `SqlLiteralScanner` (SQL/SQLite: a doubled quote is one quote, a backslash is data) behind every scanner; `ParseValue` strips one pair and un-collapses doubling; a tuple with too many values is rejected. `ParameterLiteralRoundTripTests` 74/74 |
| 8 | PK-changing UPDATE loses/duplicates a row | 🔴 High | — | **Fixed on directory storage** — every UPDATE path validates the new key first; single-file part tracked in §11 |
| 9 | Column `UNIQUE` not enforced | 🟠 Medium | — | **Fixed on directory storage** — automatic unique index, composite `UNIQUE (a, b)` included; single-file still open (format decision, §10/§11) |
| 10 | Single-file ignores `COLLATE` | 🟡 Low | — | **Open, owner decision** — needs a column-entry format extension (§10) |
| 11 | PK uniqueness / composite keys | 🔴 High | — | **Fixed on directory storage for new tables** (composite key = UNIQUE over its columns); **open, owner decision** for single-file storage, which enforces no PK uniqueness (§11) |
| 12 | Columnar scan inside a transaction misses its own UPDATE/DELETE | 🟠 Medium | — | **Fixed** — buffered overwrites and tombstones are laid over the scan (§12) |
| 13 | PageBased transaction rollback | 🔴 High | — | **Fixed** for `CancelBatchUpdate` (ADO.NET, linq2db, EF Core) via page undo images; `ExecuteBatchSQL`'s internal failure rollback still open (§13) |