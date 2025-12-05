# SharpCoreDB.EntityFrameworkCore

Entity Framework Core provider for SharpCoreDB encrypted database engine.

## Status: INCOMPLETE - Basic Infrastructure Only

This is a **work-in-progress** EF Core provider with minimal infrastructure.

### ⚠️ Known Issue: Framework Compatibility

- SharpCoreDB targets .NET 10
- EF Core 8 requires .NET 8
- Project currently won't build due to framework mismatch

**Solutions:**
1. Wait for EF Core 10 (when available)
2. Create SharpCoreDB.Compat targeting net8.0 with subset of features
3. Multi-target SharpCoreDB for both net8.0 and net10.0

This is a **work-in-progress** EF Core provider with minimal infrastructure. 

### What's Implemented ✅
- Basic connection/command/transaction classes
- Options extension framework
- UseSharpCoreDB() extension method
- Project structure and NuGet configuration

### What's Missing ❌
The following components are NOT implemented and will cause runtime errors:

1. **Type Mapping** - No SharpCoreDBTypeMappingSource
2. **Query Translation** - No LINQ to SQL translation
3. **Migrations** - No migration SQL generator
4. **Data Reader** - SharpCoreDBDataReader is a stub
5. **Database Creator** - No SharpCoreDBDatabaseCreator
6. **SQL Generation** - No query/command SQL generators
7. **Model Validation** - No SharpCoreDB-specific validators
8. **Transaction Support** - Minimal WAL integration
9. **Parameter Handling** - No proper parameter binding
10. **Result Materialization** - No query result parsing

### Usage (Will Not Work Yet)

```csharp
// This API exists but won't work without the missing components
services.AddDbContext<MyContext>(options =>
    options.UseSharpCoreDB("Data Source=app.db;Password=secret;Pooling=true"));
```

### Next Steps for Completion

A full EF Core provider requires approximately 3000-5000 lines of code across 30+ files:

1. Create SharpCoreDBTypeMappingSource (map .NET types to SQL types)
2. Create SharpCoreDBQuerySqlGenerator (LINQ expression tree → SQL)
3. Create SharpCoreDBMigrationsSqlGenerator (EF migrations → CREATE TABLE, etc.)
4. Implement SharpCoreDBDataReader with actual result parsing
5. Create SharpCoreDBDatabaseCreator for database lifecycle
6. Implement proper transaction/savepoint support
7. Add comprehensive unit tests
8. Add integration tests with sample DbContext

### Recommendation

For production use of SharpCoreDB with EF Core, consider:
- Using SharpCoreDB directly (it already has SQL support)
- Contributing to complete this provider
- Creating a dedicated development session for this feature

### Alternative: Use SharpCoreDB Directly

```csharp
var db = factory.Create("app.db", "password");
db.ExecuteSQL("CREATE TABLE TimeEntries (Id INT, StartTime DATETIME, Duration INT)");
db.ExecuteSQL("INSERT INTO TimeEntries VALUES ('1', NOW(), '480')");
```

## License

MIT - Same as SharpCoreDB main project
