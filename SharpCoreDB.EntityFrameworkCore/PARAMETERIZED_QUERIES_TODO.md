# Parameterized Queries Implementation Plan

## Current Status
The EF Core provider has parameter support infrastructure but needs integration with SQL execution for security.

## Security Risk
⚠️ **CRITICAL**: Current implementation passes SQL directly to the database without parameter substitution, creating potential SQL injection vulnerability in user-facing scenarios.

## Implementation Plan

### 1. SharpCoreDBCommand Parameter Handling

**Current State:**
```csharp
public class SharpCoreDBCommand : DbCommand
{
    protected override DbParameterCollection DbParameterCollection { get; } 
        = new SharpCoreDBParameterCollection();
    
    public override int ExecuteNonQuery()
    {
        // Currently executes SQL directly without parameter substitution
        _connection.DbInstance.ExecuteSQL(_commandText);
    }
}
```

**Required Changes:**
```csharp
public override int ExecuteNonQuery()
{
    // 1. Get parameters from DbParameterCollection
    var parameters = new Dictionary<string, object?>();
    foreach (SharpCoreDBParameter param in DbParameterCollection)
    {
        parameters[param.ParameterName] = param.Value;
    }
    
    // 2. Substitute parameters in SQL
    var sql = SubstituteParameters(_commandText, parameters);
    
    // 3. Execute sanitized SQL
    _connection.DbInstance.ExecuteSQL(sql);
}

private string SubstituteParameters(string sql, Dictionary<string, object?> parameters)
{
    // Replace @paramName or :paramName with safe values
    foreach (var param in parameters)
    {
        var value = EscapeValue(param.Value);
        sql = sql.Replace($"@{param.Key}", value)
                 .Replace($":{param.Key}", value);
    }
    return sql;
}

private string EscapeValue(object? value)
{
    if (value == null) return "NULL";
    
    return value switch
    {
        string s => $"'{s.Replace("'", "''")}'",  // Escape single quotes
        int i => i.ToString(),
        long l => l.ToString(),
        bool b => b ? "1" : "0",
        DateTime dt => $"'{dt:yyyy-MM-dd HH:mm:ss}'",
        _ => $"'{value.ToString()?.Replace("'", "''")}'"
    };
}
```

### 2. Update Query Generators

**SharpCoreDBQuerySqlGenerator:**
```csharp
protected override Expression VisitSqlParameter(SqlParameterExpression sqlParameterExpression)
{
    // Generate @paramName instead of inline values
    Sql.Append("@").Append(sqlParameterExpression.Name);
    return sqlParameterExpression;
}
```

**SharpCoreDBUpdateSqlGenerator:**
```csharp
protected override void AppendValue(StringBuilder commandStringBuilder, ColumnModification columnModification)
{
    // Use parameters instead of inline values
    var parameter = new SqlParameter($"@p{_parameterCounter++}", columnModification.Value);
    commandStringBuilder.Append(parameter.ParameterName);
    Command.Parameters.Add(parameter);
}
```

### 3. Migration SQL Generator

**SharpCoreDBMigrationsSqlGenerator:**
```csharp
protected override void Generate(InsertDataOperation operation, IModel? model, MigrationCommandListBuilder builder)
{
    // Use parameters for insert values
    foreach (var row in operation.Values)
    {
        foreach (var value in row)
        {
            var param = CreateParameter($"@p{_paramCounter++}", value);
            builder.Append(param.ParameterName);
        }
    }
}
```

### 4. SharpCoreDB Core Enhancement

**Option 1: Add native parameter support to SharpCoreDB**
```csharp
// In Database.cs
public void ExecuteSQL(string sql, Dictionary<string, object?>? parameters = null)
{
    if (parameters != null)
    {
        sql = SubstituteParameters(sql, parameters);
    }
    // Execute sanitized SQL
    ExecuteSQLInternal(sql);
}
```

**Option 2: Use prepared statement pattern**
```csharp
// In Database.cs
public PreparedStatement PrepareStatement(string sql)
{
    return new PreparedStatement(sql, this);
}

public class PreparedStatement
{
    public void SetParameter(string name, object? value);
    public void Execute();
}
```

### 5. Testing

**Security Tests:**
```csharp
[Fact]
public void ParameterizedQuery_PreventsSQL Injection()
{
    var maliciousInput = "'; DROP TABLE TimeEntries; --";
    
    // Should be safe with parameters
    var entry = context.TimeEntries
        .Where(e => e.ProjectName == maliciousInput)
        .FirstOrDefault();
    
    // Verify database still exists and contains no data matching injection
    Assert.NotNull(context.Database);
}

[Fact]
public void Update_UsesParameters()
{
    var entry = context.TimeEntries.First();
    entry.ProjectName = "Test ' with quote";
    
    // Should not throw and should properly escape
    context.SaveChanges();
    
    var updated = context.TimeEntries.Find(entry.Id);
    Assert.Equal("Test ' with quote", updated.ProjectName);
}
```

## Implementation Priority

1. **High**: Implement parameter substitution in SharpCoreDBCommand (prevents most injection attacks)
2. **Medium**: Update query generators to emit parameterized SQL
3. **Medium**: Add security tests
4. **Low**: Enhance SharpCoreDB core with native parameter support (performance optimization)

## Timeline Estimate

- Parameter substitution in Command: 2-4 hours
- Query generator updates: 4-6 hours
- Testing and validation: 2-3 hours
- **Total: 8-13 hours**

## Alternative: Use Existing ORM Features

EF Core has built-in protection against SQL injection when using LINQ. The issue only affects:
- Raw SQL queries (FromSqlRaw, ExecuteSqlRaw)
- Direct SQL string manipulation

**Recommendation**: Document that users should:
1. Always use LINQ for queries (inherently safe)
2. Use FromSqlInterpolated() for raw SQL (automatically parameterizes)
3. Never concatenate user input into SQL strings

## Example Safe Usage

```csharp
// ✅ SAFE: LINQ query
var entries = context.TimeEntries
    .Where(e => e.ProjectName == userInput)
    .ToList();

// ✅ SAFE: Interpolated SQL
var entries = context.TimeEntries
    .FromSqlInterpolated($"SELECT * FROM TimeEntries WHERE ProjectName = {userInput}")
    .ToList();

// ❌ UNSAFE: String concatenation
var entries = context.TimeEntries
    .FromSqlRaw($"SELECT * FROM TimeEntries WHERE ProjectName = '{userInput}'")
    .ToList();
```

## References

- [EF Core SQL Injection](https://learn.microsoft.com/en-us/ef/core/querying/sql-queries#passing-parameters)
- [Parameterized Queries Best Practices](https://cheatsheetseries.owasp.org/cheatsheets/SQL_Injection_Prevention_Cheat_Sheet.html)
- [ADO.NET Parameters](https://learn.microsoft.com/en-us/dotnet/api/system.data.sqlclient.sqlparameter)

---

**Status**: Infrastructure ready, implementation pending
**Priority**: High for production use
**Complexity**: Medium (8-13 hours estimated)
