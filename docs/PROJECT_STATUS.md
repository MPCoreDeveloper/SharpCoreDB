# SharpCoreDB Project Status

**Version:** 1.5.0  
**Status:** ⚠️ Production Ready with One Deferred Engine Limitation  
**Last Updated:** March 14, 2026

## 🎯 Current Status

> **Implementation audit (March 14, 2026):** Engine limitation remediation pass completed for parser/runtime behavior. Five previously documented gaps are now implemented and validated. One disposal deadlock scenario remains deferred (see below).

SharpCoreDB is a **production-ready, high-performance embedded AND networked database** for .NET 10 with enterprise-scale distributed capabilities, server mode, and advanced GraphRAG analytics.

### ✅ Resolved During Latest Remediation Pass

- `IS NULL` / `IS NOT NULL` evaluation parity across runtime scan, join helper path, and compiled predicate path.
- German locale `ß/ss` equivalence in locale-aware equality via `CompareOptions.IgnoreNonSpace`.
- Scalar function parsing in SELECT columns (including `COALESCE(...)`) via expression-backed column parsing.
- Enhanced SQL parser trailing-token validation to reliably set `HasErrors` on malformed trailing content.
- LINQ translator support for `ExpressionType.Convert`/`ConvertChecked` in enum-related comparisons.

### ⚠️ Deferred Limitation (Tracked)

- `SingleFileDatabase.ExecuteCompiled` with parameterized plans can still hang in specific disposal/shutdown paths.
- Mitigations were applied (safer disposal ordering), but a full fix requires async disposal lifecycle refactoring (`IAsyncDisposable`) in single-file storage provider internals.

### 🧪 Validation Baseline

- Latest CI-style run: **1,490 passed, 0 failed, 0 skipped**.
- Locale-collation suite: **21 passed, 0 failed, 0 skipped**.

### Follow-up Audit Index

Follow-up implementation backlog (broader than the engine-limit pass) remains tracked in:
- `docs/TODO_AUDIT_2026-03.md`
