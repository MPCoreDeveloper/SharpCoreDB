# OPTIONALLY SQL + Option<T> support (v1.7.0)

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
