# 10. Providers & Adapters

> SharpCoreDB plugs into the standard .NET data ecosystem. Deep dives:
> [`src/SharpCoreDB.Data.Provider/README.md`](../../src/SharpCoreDB.Data.Provider/README.md) ·
> [`src/SharpCoreDB.EntityFrameworkCore/README.md`](../../src/SharpCoreDB.EntityFrameworkCore/README.md) ·
> [`docs/efcore-provider/Guid-Navigation-Support.md`](../efcore-provider/Guid-Navigation-Support.md)

---

## 10.1 ADO.NET provider (`SharpCoreDB.Data.Provider`)

A first-class `DbProviderFactory` implementation: `SharpCoreDBConnection`,
`SharpCoreDBCommand`, `SharpCoreDBDataReader`. Works with Dapper and other ADO.NET tooling.

```csharp
using var conn = new SharpCoreDBConnection(connectionString);
conn.Open();
using var cmd = conn.CreateCommand();
cmd.CommandText = "SELECT id, name FROM customers WHERE name = @n";
cmd.Parameters.AddWithValue("@n", "Ada");
using var reader = cmd.ExecuteReader();
while (reader.Read())
    Console.WriteLine($"{reader.GetInt32(0)} {reader.GetString(1)}");
```

v2.0 provider fast paths:
- `OPTIONALLY` keyword check avoids a full SQL parse per `ExecuteReader`
- span-based single-file mode detection and `sqlite_master`-compatibility detection
  (no per-call allocations)

## 10.2 EF Core provider (`SharpCoreDB.EntityFrameworkCore`)

- Full `IQueryable` LINQ support, migrations, Guid/ULID keyed entities
- Stable relationship materialization (two-query pattern) for navigation properties
- 22/22 integration tests including end-to-end seed CRUD

```csharp
services.AddDbContext<AppContext>(o =>
    o.UseSharpCoreDB(@"C:\data\appdb.scdb", password: "s3cret!"));
```

Guide: [`docs/graphrag/EF_CORE_COMPLETE_GUIDE.md`](../graphrag/EF_CORE_COMPLETE_GUIDE.md) ·
Bug documentation: [`docs/issues/efcore-guid-navigation-bug.md`](../issues/efcore-guid-navigation-bug.md)

## 10.3 Functional adapters

| Package | Adapter | Highlights |
|---------|---------|-----------|
| `SharpCoreDB.Functional.Dapper` | Dapper | `Option<T>`, `Fin<T>`, `Seq<T>` results |
| `SharpCoreDB.Functional.EntityFrameworkCore` | EF Core | functional composition over `IQueryable` |
| `SharpCoreDB.Functional.Linq2DB` | linq2db | `BulkCopyAsync` batching, full type mapping (ULID/GUID/DateTime), compile-safe LINQ — ideal for high-throughput AI/agentic workloads |

```csharp
// linq2db functional example
var result = await db.GetTable<Customer>()
    .Where(c => c.Age >= 18)
    .ToFinAsync();
```

See [`src/SharpCoreDB.Functional.Linq2DB/README.md`](../../src/SharpCoreDB.Functional.Linq2DB/README.md).

## 10.4 YesSql / OrchardCore

`SharpCoreDB.Provider.YesSql` provides the YesSql abstraction used by OrchardCore CMS.

## 10.5 Dotmim.Sync (`SharpCoreDB.Provider.Sync`)

Bidirectional cloud/edge data sync provider built on Dotmim.Sync. Docs:
[`docs/sync/README.md`](../sync/README.md) · [`docs/sync/CHANGELOG.md`](../sync/CHANGELOG.md)

## 10.6 Migrations (`SharpCoreDB.FluentMigrator`)

FluentMigrator integration for embedded and server modes:

```csharp
[Maintenance(MigrationStage.BeforeAll)]
[Migration(202601011200)]
public class CreateCustomers : Migration
{
    public override void Up() => Create.Table("customers")
        .WithColumn("id").AsInt64().PrimaryKey()
        .WithColumn("name").AsString(200).NotNullable();
    public override void Down() => Delete.Table("customers");
}
```

Docs: [`docs/migration/FLUENTMIGRATOR_EMBEDDED_MODE_v1.7.0.md`](../migration/FLUENTMIGRATOR_EMBEDDED_MODE_v1.7.0.md) ·
[`docs/migration/FLUENTMIGRATOR_SERVER_MODE_v1.7.0.md`](../migration/FLUENTMIGRATOR_SERVER_MODE_v1.7.0.md)
