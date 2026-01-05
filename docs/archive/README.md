# Archived Documentation

This directory contains obsolete or superseded documentation that has been archived for historical reference.

## Why These Files Were Archived

These documents were moved here because they:
- Describe completed refactoring work
- Are superseded by consolidated documentation
- Contain duplicate information now in organized locations
- Are historical status reports no longer needed

## Current Documentation

For current, up-to-date documentation, see:

- [Main Documentation](../README.md) - Documentation index
- [Feature Status](../FEATURE_STATUS.md) - Current feature matrix
- [Query Plan Cache](../QUERY_PLAN_CACHE.md) - Query caching guide
- [SIMD Optimizations](../SIMD_OPTIMIZATION_SUMMARY.md) - SIMD guide
- [SCDB Format](../scdb/) - Single-file format documentation
- [Migration Guide](../migration/MIGRATION_GUIDE.md) - Migration documentation

## Archived Files

| File | Archived Date | Superseded By |
|------|---------------|---------------|
| `QUERY_COMPILER_REFACTOR.md` | 2026-01-XX | Completed - integrated into codebase |
| `SQL_PARSING_REFACTOR.md` | 2026-01-XX | Completed - integrated into codebase |
| `REFACTORING_COMPLETE.md` | 2026-01-XX | Historical status report |
| `REORGANIZATION_COMPLETE.md` | 2026-01-XX | Historical status report |
| `QUICK_REFERENCE.md` | 2026-01-XX | `FEATURE_STATUS.md` |
| `CLEANUP_SUMMARY.md` | 2026-01-XX | Historical status report |
| `SCDB_IMPLEMENTATION_STATUS.md` | 2026-01-XX | `scdb/IMPLEMENTATION_STATUS.md` |
| `SCDB_PHASE1_IMPLEMENTATION.md` | 2026-01-XX | `scdb/PHASE1_IMPLEMENTATION.md` |
| `SCDB_COMPILATION_FIXES.md` | 2026-01-XX | `development/SCDB_COMPILATION_FIXES.md` |
| `SCDB_COMPILATION_FIXES_NL.md` | 2026-01-XX | `development/SCDB_COMPILATION_FIXES_NL.md` |

## Retrieval

If you need to restore any of these files, they are preserved in Git history:

```bash
# View archived files
git log --all --full-history -- docs/archive/

# Restore specific file
git checkout <commit-hash> -- docs/archive/<filename>
```

## Note

These files are kept for:
- Historical reference
- Understanding the evolution of the project
- Recovery if needed

They are **not maintained** and may contain **outdated information**.

---

**Archive Created:** 2026-01-XX  
**Reason:** Documentation consolidation and reorganization
