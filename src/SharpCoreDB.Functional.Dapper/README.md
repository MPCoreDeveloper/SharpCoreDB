# SharpCoreDB.Functional.Dapper

Dapper adapter package for `SharpCoreDB.Functional`.

## Purpose

This module provides functional wrappers over Dapper operations:

- `Task<Option<T>>` for optional reads
- `Task<Fin<Unit>>` for write operations
- `Task<Seq<T>>` for sequence-based queries

## Entry points

- `IDbConnection.Functional()`
- `IDatabase.FunctionalDapper()`

## Production dependency model

In production NuGet usage, this package references `SharpCoreDB.Functional` and `SharpCoreDB.Extensions` as package dependencies, which provide transitive core references.
