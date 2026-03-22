# SharpCoreDB.Functional.EntityFrameworkCore

Entity Framework Core adapter package for `SharpCoreDB.Functional`.

## Purpose

This module provides functional wrappers over `DbContext` operations:

- `Task<Option<T>>` for optional reads
- `Task<Fin<Unit>>` for write operations
- `Task<Seq<T>>` for sequence-based queries

## Entry points

- `DbContext.Functional()`

## Production dependency model

In production NuGet usage, this package references `SharpCoreDB.Functional` and `SharpCoreDB.EntityFrameworkCore` as package dependencies, which provide transitive core references.
