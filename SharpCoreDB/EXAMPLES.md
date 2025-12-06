# SharpCoreDB Examples

## Parameterized Queries

Parameterized queries help prevent SQL injection by separating SQL code from data.

### Example: Insert with Parameters

```csharp
using SharpCoreDB;

var db = new DatabaseFactory(services).Create("path/to/db", "password");
var parameters = new Dictionary<string, object?>
{
    ["0"] = "John Doe",
    ["1"] = 30
};
await db.ExecuteSQLAsync("INSERT INTO users (name, age) VALUES (?, ?)", parameters);
```

### Example: Select with Parameters

```csharp
var parameters = new Dictionary<string, object?>
{
    ["0"] = "John Doe"
};
await db.ExecuteSQLAsync("SELECT * FROM users WHERE name = ?", parameters);
```

## Extended SQL Support

### LIMIT and OFFSET

```csharp
// Get first 10 users
db.ExecuteSQL("SELECT * FROM users LIMIT 10");

// Skip first 5, get next 10
db.ExecuteSQL("SELECT * FROM users LIMIT 10 OFFSET 5");
```

### ORDER BY with Indexes

Create an index for faster sorting:

```csharp
db.ExecuteSQL("CREATE INDEX idx_age ON users (age)");
db.ExecuteSQL("SELECT * FROM users ORDER BY age DESC");
```

### Subqueries

Subqueries in WHERE clauses are supported:

```csharp
db.ExecuteSQL("SELECT * FROM users WHERE age > (SELECT AVG(age) FROM users)");
```
