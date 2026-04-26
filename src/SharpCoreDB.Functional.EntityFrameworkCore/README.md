# SharpCoreDB.Functional.EntityFrameworkCore

Entity Framework Core adapter for `SharpCoreDB.Functional`.

**Version:** `v1.7.1`  
**Package:** `SharpCoreDB.Functional.EntityFrameworkCore`


## Patch updates in v1.7.1

- ✅ Aligned package metadata and version references to the synchronized 1.7.1 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.

## Features

- Functional wrappers over `DbContext` operations
- `Task<Option<T>>` for optional reads
- `Task<Fin<Unit>>` for write operations
- `Task<Seq<T>>` for sequence-based query results
- Complements `SharpCoreDB.EntityFrameworkCore` provider usage

## Changes in v1.7.1

- Functional EF Core adapter introduced in `v1.7.1`
- Documentation aligned with modular functional package family
- Keeps dependencies optional and transitive through package references

## Installation

```bash
dotnet add package SharpCoreDB.Functional.EntityFrameworkCore --version 1.7.1
```

## Documentation

- `docs/INDEX.md`
- `src/SharpCoreDB.Functional/README.md`

