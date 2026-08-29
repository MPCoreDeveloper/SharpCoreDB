# 13. Advanced Features, Integration & Troubleshooting

> Stored procedures, views, triggers, integration patterns, troubleshooting, and best practices.
> This chapter consolidates the legacy `docs/USER_MANUAL.md` into the v2.0 manual.

---

## 13.1 Stored procedures

Procedures encapsulate business logic in the database, with `IN`/`OUT` parameters,
`DECLARE`, `IF/ELSE`, and `SET`:

```sql
CREATE PROCEDURE transfer_funds (
    IN from_account INT,
    IN to_account   INT,
    IN amount       REAL,
    OUT success     INT
) AS
BEGIN
    DECLARE balance REAL;
    SELECT balance FROM accounts WHERE id = from_account INTO balance;

    IF balance >= amount THEN
        UPDATE accounts SET balance = balance - amount WHERE id = from_account;
        UPDATE accounts SET balance = balance + amount WHERE id = to_account;
        SET success = 1;
    ELSE
        SET success = 0;
    END IF;
END
```

```csharp
var result = db.ExecuteSQL("EXEC transfer_funds(1, 2, 100.00)");
// OUT parameters are accessible via the result dictionary
```

## 13.2 Views & materialized views

Views act as virtual tables; materialized views pre-compute results:

```sql
CREATE VIEW active_users AS
SELECT id, name, email, age FROM users WHERE age >= 18;

CREATE MATERIALIZED VIEW user_stats AS
SELECT city, COUNT(*) AS user_count, AVG(age) AS avg_age
FROM users
GROUP BY city;
```

```csharp
var rows = db.ExecuteQuery("SELECT * FROM active_users WHERE age > 25");
db.ExecuteSQL("DROP VIEW active_users");
```

## 13.3 Triggers

Triggers run automatically on data changes:

```sql
-- AFTER UPDATE — audit trail
CREATE TRIGGER audit_user_changes
AFTER UPDATE ON users
BEGIN
    INSERT INTO audit_log (table_name, action, timestamp, old_data, new_data)
    VALUES ('users', 'UPDATE', CURRENT_TIMESTAMP, OLD.*, NEW.*)
END;

-- BEFORE INSERT — validation
CREATE TRIGGER validate_age
BEFORE INSERT ON users
BEGIN
    IF NEW.age < 0 OR NEW.age > 150 THEN
        RAISE ERROR 'Invalid age'
    END IF
END;

DROP TRIGGER audit_user_changes;
```

## 13.4 Integration patterns

### ASP.NET Core

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSharpCoreDB();
builder.Services.AddScoped<IUserRepository, UserRepository>();
var app = builder.Build();

// One database instance per request (factory-managed)
app.MapGet("/users", (DatabaseFactory factory) =>
{
    using var db = factory.Create("./app.db", "password");
    return Results.Ok(db.ExecuteQuery("SELECT * FROM users"));
});
app.Run();
```

### Repository pattern

```csharp
public class UserRepository
{
    private readonly DatabaseFactory _factory;
    public UserRepository(DatabaseFactory factory) => _factory = factory;

    public User? GetById(int id)
    {
        using var db = _factory.Create("./app.db", "password");
        var row = db.FindByPrimaryKey("users", key: id);  // ⚡ Direct API fast path
        return row != null ? MapToUser(row) : null;
    }

    public void Add(User user)
    {
        using var db = _factory.Create("./app.db", "password");
        db.Insert("users", new Dictionary<string, object>
        {
            ["name"] = user.Name, ["email"] = user.Email, ["age"] = user.Age
        });
        db.Flush();
    }
}
```

> ⚡ For hot paths, prefer `FindByPrimaryKey`/`ExecuteQueryStruct` and reuse the `IDatabase`
> instance instead of opening one per call — see the [Performance Guide](performance.md).

### Unit testing

```csharp
[TestClass]
public class UserRepositoryTests
{
    [TestMethod]
    public void GetById_WithValidId_ReturnsUser()
    {
        var factory = new DatabaseFactory(services: new ServiceCollection().BuildServiceProvider());
        var dbPath = Path.Combine(Path.GetTempPath(), $"scdb-test-{Guid.NewGuid()}");
        using var db = factory.Create(dbPath, masterPassword: "test");
        db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT, email TEXT, age INTEGER)");
        db.ExecuteSQL("INSERT INTO users VALUES (1, 'Alice', 'alice@example.com', 30)");
        db.Flush();

        var user = new UserRepository(factory).GetById(1);

        Assert.IsNotNull(user);
        Assert.AreEqual("Alice", user.Name);
    }
}
```

## 13.5 Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| "Database is locked" | Multiple instances open the same file | One `IDatabase` instance per application path |
| Data lost after restart | Missing `Flush()`/`Commit` | Call `db.Flush()` after write batches |
| Out of memory on large reads | Whole-result materialization | Use `LIMIT @n OFFSET @m` pagination or streaming |
| Slow queries | Full scans on non-indexed predicates | `CREATE INDEX` on filtered columns; hash for `=`, B-tree for ranges |
| File corruption | Unexpected shutdown | WAL auto-recovers; see `docs/scdb/PRODUCTION_GUIDE.md` for `.scdb` repair |

## 13.6 Best practices

1. **Always `using`/dispose** `IDatabase` instances.
2. **Parameterize all SQL** (`@name`) — prevents injection and hits the plan cache.
3. **Batch writes** (`InsertBatch`, `UpdateMultiple`, `ExecuteBatchSQL`) instead of per-row loops.
4. **Index hot lookup columns** — hash index for point lookups, B-tree for ranges.
5. **Flush after write batches** — one durability point per batch.
6. **Use transactions** for multi-statement operations (`BeginTransaction`/`Commit`).
7. **Monitor database size** and plan retention (`db.GetTables()`, `db.VacuumAsync()`).
8. **Back up regularly** — server-mode backup/restore runbook:
   `docs/server/MULTITENANT_BACKUP_RESTORE_MIGRATION_v1.7.0.md`; `.scdb` repair:
   `docs/scdb/PRODUCTION_GUIDE.md`.
9. **Benchmark on the target machine** — see [Performance Guide](performance.md).
