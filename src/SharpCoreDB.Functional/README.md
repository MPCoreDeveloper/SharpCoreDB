# SharpCoreDB.Functional

Functional facade for SharpCoreDB built on .NET 10 + C# 14 with LanguageExt.

## MVP API

- `Database.Functional()` / `IDatabase.Functional()` entry points
- `GetByIdAsync<T>(...) -> Task<Option<T>>`
- `FindOneAsync<T>(...) -> Task<Option<T>>`
- `QueryAsync<T>(...) -> Task<Seq<T>>`
- `InsertAsync<T>(...) -> Task<Fin<Unit>>`
- `UpdateAsync<T>(...) -> Task<Fin<Unit>>`
- `DeleteAsync(...) -> Task<Fin<Unit>>`
- `CountAsync(...) -> Task<long>`

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
```
