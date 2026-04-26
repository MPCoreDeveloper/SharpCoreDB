# SharpCoreDB.Functional.Dapper

Dapper adapter for `SharpCoreDB.Functional`.

**Version:** `v1.7.1`  
**Package:** `SharpCoreDB.Functional.Dapper`


## Patch updates in v1.7.1

- ✅ Aligned package metadata and version references to the synchronized 1.7.1 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.

## Features

- Functional wrappers over Dapper operations
- `Task<Option<T>>` for optional reads
- `Task<Fin<Unit>>` for write operations
- `Task<Seq<T>>` for sequence-based query results
- Entry points for `IDbConnection` and `IDatabase` integration

## Changes in v1.7.1

- Functional Dapper adapter introduced in `v1.7.1`
- Documentation aligned to optional modular architecture
- Keeps production dependencies flowing through transitive package references

## Installation

```bash
dotnet add package SharpCoreDB.Functional.Dapper --version 1.7.1
```

## Documentation

- `docs/INDEX.md`
- `src/SharpCoreDB.Functional/README.md`

