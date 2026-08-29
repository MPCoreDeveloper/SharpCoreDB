# Storage & Engine Guide

> How SharpCoreDB stores, encrypts, and persists data — modes, engines, single-file format,
> and metadata. Deep dives below.

---

## Storage modes

| Mode | When to use | Docs |
|------|-------------|------|
| **Directory mode** (default) | OLTP with many tables; per-record encryption; debuggability | this page |
| **Single-file `.scdb`** | Portable / app-store scenarios; one encrypted file | [SINGLE_FILE_SQL_LIMITATIONS.md](SINGLE_FILE_SQL_LIMITATIONS.md) |
| **Columnar storage** | Analytics / SIMD aggregates | [`../analytics/README.md`](../analytics/README.md) |

## Engine modes

| Engine | Best for | Docs |
|--------|----------|------|
| Append-only | Bulk ingestion, append-heavy writes | [STORAGE_MODE_GUIDANCE.md](STORAGE_MODE_GUIDANCE.md) |
| Page-based | OLTP with many in-place updates | [STORAGE_MODE_GUIDANCE.md](STORAGE_MODE_GUIDANCE.md) |

## Topics

- **Storage quick reference** (engine flags, modes, trade-offs):
  [QUICK_REFERENCE_v1.7.0.md](QUICK_REFERENCE_v1.7.0.md)
- **Metadata behavior & improvements** (catalog, headers, versioning):
  [METADATA_IMPROVEMENTS_v1.7.0.md](METADATA_IMPROVEMENTS_v1.7.0.md)
- **Single-file SQL support & limitations**:
  [SINGLE_FILE_SQL_LIMITATIONS.md](SINGLE_FILE_SQL_LIMITATIONS.md)
- **Serialization & binary formats**:
  [`../serialization/README.md`](../serialization/README.md) ·
  [`../serialization/SERIALIZATION_AND_STORAGE_GUIDE.md`](../serialization/SERIALIZATION_AND_STORAGE_GUIDE.md)

## Manual

See the [Database Core](../manual/database.md) chapter of the v2.0 manual for API-level guidance,
transactions & WAL, and AES-256-GCM encryption.
