# SharpCoreDB with OrchardCore CMS

## Overview

SharpCoreDB is a lightweight, single-file SQL database provider for OrchardCore CMS. This integration uses YesSql as the ORM layer and SQLite-compatible SQL syntax, making it ideal for development, testing, and embedded scenarios.

## Getting Started

### Quick Start (5 minutes)

1. **Install the NuGet packages:**
   ```bash
   dotnet add package SharpCoreDB
   dotnet add package SharpCoreDB.Data.Provider
   dotnet add package SharpCoreDB.Provider.YesSql
   ```

2. **Register SharpCoreDB in Program.cs:**
   ```csharp
   // Register SharpCoreDB provider factory
   SharpCoreDbConfigurationExtensions.RegisterProviderFactory();
   
   // Add OrchardCore
   builder.Services.AddOrchardCms();
   ```

3. **Configure in appsettings.json:**
   ```json
   {
     "OrchardCore": {
       "OrchardCore_Default": {
         "DatabaseProvider": "Sqlite",
         "ConnectionString": "Data Source=App_Data/Sites/Default/site.scdb;Password=mypassword",
         "TablePrefix": "OC_"
       }
     }
   }
   ```

4. **Run the application:**
   ```bash
   dotnet run
   ```

5. **Complete the setup wizard** that appears on first launch.

### Full Example

See `SharpCoreDb.Orchardcore` project in this repository for a complete working example.

## Database Provider Configuration

### Connection String Format

```
Data Source=path/to/database.scdb;Password=yourpassword
```

- **Data Source**: Path to the database file (absolute or relative)
- **Password**: Encryption password for SharpCoreDB (optional)

### appsettings.json Configuration

```json
{
  "OrchardCore": {
    "OrchardCore_Default": {
      "DatabaseProvider": "Sqlite",
      "ConnectionString": "Data Source=App_Data/Sites/Default/site.scdb;Password=orchardcore",
      "TablePrefix": "OC_",
      "UseTableNamePrefixForTemporaryTables": true,
      "ConnectionRetryCount": 5,
      "ConnectionRetryDelayMilliseconds": 500
    }
  }
}
```

### Important Notes

1. **Database Provider**: Use `"Sqlite"` - SharpCoreDB is SQLite-compatible
2. **Table Prefix**: Recommended to use `"OC_"` for OrchardCore tables
3. **Connection Pooling**: Automatically managed by SharpCoreDB
4. **Transaction Isolation**: Default is `ReadCommitted`

## How It Works

### Architecture

```
OrchardCore CMS
    ↓
Shell System (manages tenants)
    ↓
YesSql ORM
    ↓
SharpCoreDB Provider
    ↓
SQLite-Compatible Database File
```

### Initialization Flow

1. **Provider Registration**: `RegisterProviderFactory()` registers SharpCoreDB with ADO.NET
2. **OrchardCore Detection**: Reads configuration from appsettings.json
3. **Setup Wizard**: On fresh databases, shows setup configuration
4. **Shell Creation**: After configuration, creates tenant shell
5. **Store Initialization**: Shell creates YesSql store for the tenant
6. **Schema Creation**: YesSql creates necessary tables
7. **Application Ready**: Site is ready to use

### Key Features

✅ **SQLite Compatible**: Uses SQLite SQL syntax and semantics  
✅ **Single-File Database**: Everything in one `.scdb` file  
✅ **OrchardCore Native**: Works with OrchardCore's shell system  
✅ **YesSql Integration**: Full ORM support through YesSql  
✅ **Automatic Schema**: Tables created automatically by YesSql  
✅ **Multi-Tenant Ready**: Supports OrchardCore's multi-tenancy  
✅ **Thread-Safe**: Built-in connection pooling and synchronization  

## Configuration Details

### Table Prefix

All OrchardCore tables are prefixed with the configured prefix:

```sql
OC_ContentItem
OC_ContentItemVersion
OC_Document
...
```

### Isolation Level

Default is `ReadCommitted`. Can be configured in code:

```csharp
services.AddYesSqlWithSharpCoreDB(
    connectionString: "Data Source=...",
    tablePrefix: "OC_",
    isolationLevel: System.Data.IsolationLevel.ReadCommitted
);
```

## Development

### Project Structure

```
SharpCoreDB.Provider.YesSql/
├── YesSqlConfigurationExtensions.cs   # Main integration
├── SharpCoreDbConnectionFactory.cs    # Connection management
└── README.md                           # Provider documentation

SharpCoreDb.Orchardcore/
├── Program.cs                          # Setup and configuration
├── appsettings.json                    # Database configuration
├── SharpCoreDbSetupHelper.cs          # Setup utilities
└── App_Data/                           # Database files
```

### Manual Configuration

If you need fine-grained control over YesSql configuration:

```csharp
services.AddSingleton<YesSql.IConfiguration>(sp =>
{
    var config = new YesSql.Configuration();
    config.UseSharpCoreDB(
        connectionString: "Data Source=...",
        tablePrefix: "OC_",
        isolationLevel: IsolationLevel.ReadCommitted
    );
    return config;
});
```

## Troubleshooting

### Database File Not Created

Ensure the `App_Data/Sites/Default/` directory exists and is writable.

### Connection String Invalid

Check that:
- Path is correct (absolute or relative from app root)
- No special characters (or properly escaped)
- File extension is `.scdb`

### Setup Wizard Not Appearing

- Delete `App_Data/Sites/Default/site.scdb`
- Restart the application
- Setup wizard should appear

### Slow Startup

First startup creates schema - subsequent startups are much faster. If slow:
- Check appsettings.json for correct configuration
- Verify database file location
- Check disk I/O performance

## FAQ

**Q: Can I use SharpCoreDB in production?**  
A: SharpCoreDB is suitable for small to medium deployments. For high-traffic sites, consider SQL Server or PostgreSQL.

**Q: How do I backup the database?**  
A: Simply copy the `.scdb` file. It's a single file, making backups simple.

**Q: Can I migrate from SQLite to SharpCoreDB?**  
A: Yes. The schemas are compatible. Export from SQLite and import to SharpCoreDB.

**Q: What's the maximum database size?**  
A: SharpCoreDB supports databases up to terabytes in size.

**Q: Can I use this with Docker?**  
A: Yes. Mount a volume for `App_Data/` to persist the database.

## Performance

### Startup Time
- First run: 2-3 seconds (schema creation)
- Subsequent runs: < 500ms (cached schema)

### Query Performance
- Comparable to SQLite
- All standard SQL optimizations apply

### Concurrency
- Supports multiple concurrent connections
- Automatic connection pooling
- Transaction support with configurable isolation levels

## Support

For issues or questions:
1. Check the [troubleshooting](#troubleshooting) section
2. Review the example project: `SharpCoreDb.Orchardcore`
3. Check [SharpCoreDB documentation](https://github.com/MPCoreDeveloper/SharpCoreDB)
4. Open an issue on GitHub

## License

SharpCoreDB is licensed under the MIT License. See LICENSE file for details.

---

**Ready to use?** Start with the [Quick Start](#quick-start) section above!
