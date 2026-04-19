# SharpCoreDB.Functional

Functional facade for `SharpCoreDB`.

**Version:** `v1.7.0`  
**Package:** `SharpCoreDB.Functional`

## Features

- Functional wrappers for database workflows
- Core types: `Option<T>`, `Fin<T>`, `Seq<T>`, and `Unit`
- Functional query/command style extensions over `Database` and `IDatabase`
- Works as base module for Dapper and EF Core functional adapters

## Changes in v1.7.0

- Functional package introduced and aligned to `v1.7.0`
- Documentation aligned with modular adapter ecosystem
- Maintains optional architecture with transitive dependency flow

## Installation

```bash
dotnet add package SharpCoreDB.Functional --version 1.7.0
```

## Related packages

- `SharpCoreDB.Functional.Dapper`
- `SharpCoreDB.Functional.EntityFrameworkCore`

## Documentation

- `docs/INDEX.md`

## MVP API

- `Database.Functional()` / `IDatabase.Functional()` entry points
- `GetByIdAsync<T>(...) -> Task<Option<T>>`
- `FindOneAsync<T>(...) -> Task<Option<T>>`
- `QueryAsync<T>(...) -> Task<Seq<T>>`
- `InsertAsync<T>(...) -> Task<Fin<Unit>>`
- `UpdateAsync<T>(...) -> Task<Fin<Unit>>`
- `DeleteAsync(...) -> Task<Fin<Unit>>`
- `CountAsync(...) -> Task<long>`

## Functional SQL Syntax (v1.7.0)

The functional facade supports SQL extensions that map directly to `Option<T>` behavior.

### Example

```sql
SELECT Id, Name, Email OPTIONALLY FROM Users
WHERE Email IS SOME;
```

### Execute from C#

```csharp
var fdb = database.Functional();

var users = await fdb.ExecuteFunctionalSqlAsync<UserDto>(
    "SELECT Id, Name, Email OPTIONALLY FROM Users WHERE Email IS SOME");
```

### Supported keywords

- `OPTIONALLY FROM`
- `IS SOME`
- `IS NONE`
- `MATCH SOME <column>`
- `MATCH NONE <column>`
- `UNWRAP <column> AS <alias> [DEFAULT '<value>']`

### Verification tests

```bash
dotnet test tests/SharpCoreDB.Functional.Tests --filter "FullyQualifiedName~FunctionalSqlSyntaxTests"
```

Source: `tests/SharpCoreDB.Functional.Tests/FunctionalSqlSyntaxTests.cs`

## Chaining example

```csharp
var dbf = database.Functional();

var result = await dbf
    .GetByIdAsync<User>("Users", 42, cancellationToken: ct)
    .Map(opt => opt.Map(user => user with { LastSeenUtc = DateTime.UtcNow }))
    .Map(opt => opt.ToFin("User not found"));

result.Match(
    Succ: _ => Console.WriteLine("updated"),
    Fail: err => Console.WriteLine(err.Message));
