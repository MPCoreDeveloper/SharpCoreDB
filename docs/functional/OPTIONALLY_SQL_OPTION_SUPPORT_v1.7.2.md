# OPTIONALLY SQL + Option<T> support (v1.7.2)

SharpCoreDB now supports native optional-value query shaping for functional applications.

## SQL syntax

```sql
SELECT id, name, email OPTIONALLY
FROM users
WHERE email IS SOME;
```

Supported predicates:

- `IS SOME` → matches non-null values
- `IS NONE` → matches null/DBNull values

## Behavior

When `OPTIONALLY` is used in `SELECT`, the ADO.NET provider maps projected values to `Option<T>` from `SharpCoreDB.Functional`:

- Non-null value → `Option.Some<T>(value)`
- Null value → `Option.None<T>()`

Type inference rules:

- Uses first non-null value in each column to infer inner `T`
- If all values are null, defaults to `Option<object>`

## Integration with SharpCoreDB.Functional.Linq2DB (Production Recommendation)

The new `SharpCoreDB.Functional.Linq2DB` package complements the `OPTIONALLY` SQL syntax perfectly:

```csharp
var db = new FunctionalLinq2DbContext(connection);

// Use linq2db for type-safe queries with functional return types
var userOpt = await db.FindOneAsync<User>(u => u.Email == "test@example.com"); // Option<User>
var users = await db.QueryAsync<User>(u => u.IsActive); // Seq<User>

// Or combine with raw OPTIONALLY SQL via the core Functional API
var optionalUsers = await functionalDb.ExecuteFunctionalSqlAsync<User>(
    "SELECT id, name, email OPTIONALLY FROM users WHERE email IS SOME");
```

**Benefits**:
- Compile-time safety + railway-oriented error handling (`Fin<T>`)
- High-performance BulkCopy for batch GraphRAG ingestion
- Full mapping support for ULID, GUID, DateTimeOffset, etc.

See `src/SharpCoreDB.Functional.Linq2DB/README.md` for complete API.

## CQRS / Event Sourcing advantage

`OPTIONALLY` reduces defensive null-checks in handlers and projections.

### Without OPTIONALLY

```csharp
var emailObj = reader["email"];
if (emailObj is null || emailObj is DBNull)
{
    // fallback path
}
else
{
    var email = emailObj.ToString();
    // normal path
}
```

### With OPTIONALLY

```csharp
var emailOpt = (Option<string>)reader["email"];
var normalized = emailOpt.Match(
    Some: email => email.Trim().ToLowerInvariant(),
    None: () => "(missing)");
```

This aligns naturally with functional pipelines used in CQRS command handlers, read models, and event upcasters.
