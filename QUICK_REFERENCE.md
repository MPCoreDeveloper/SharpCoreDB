# Quick Reference: SharpCoreDB with OrchardCore

## Setup in 3 Steps

### Step 1: Register Provider (Program.cs)
```csharp
// Register SharpCoreDB provider factory
SharpCoreDbConfigurationExtensions.RegisterProviderFactory();
SharpCoreDbSetupHelper.EnsureDatabaseFileExists(defaultConnection);

// Add OrchardCore (that's it!)
builder.Services.AddOrchardCms();
```

### Step 2: Configure Database (appsettings.json)
```json
{
  "OrchardCore": {
    "OrchardCore_Default": {
      "DatabaseProvider": "Sqlite",
      "ConnectionString": "Data Source=App_Data/Sites/Default/site.scdb;Password=orchardcore",
      "TablePrefix": "OC_"
    }
  }
}
```

### Step 3: Run
```bash
dotnet run
# Setup wizard will appear automatically
```

## Common Tasks

### Change Database Location
**appsettings.json:**
```json
{
  "OrchardCore": {
    "OrchardCore_Default": {
      "ConnectionString": "Data Source=/path/to/database.scdb;Password=..."
    }
  }
}
```

### Backup Database
```bash
# Copy the single database file
cp App_Data/Sites/Default/site.scdb backups/site-backup.scdb
```

### Reset Database
```bash
# Delete the file, app will recreate it
rm App_Data/Sites/Default/site.scdb
dotnet run
# Complete setup wizard again
```

### Connection String Variations

**Default (password protected):**
```
Data Source=App_Data/Sites/Default/site.scdb;Password=mypassword
```

**No password:**
```
Data Source=App_Data/Sites/Default/site.scdb
```

**Relative path:**
```
Data Source=App_Data/site.scdb;Password=xxx
```

**Absolute path:**
```
Data Source=C:/Data/site.scdb;Password=xxx
```

## Troubleshooting

| Problem | Solution |
|---------|----------|
| **Setup wizard doesn't appear** | Delete `App_Data/Sites/Default/site.scdb`, restart app |
| **Connection string error** | Check path exists, verify password is correct |
| **Permission denied** | Ensure app can write to `App_Data/` directory |
| **Slow startup** | First run creates schema (~3 sec), normal after |
| **Database locked** | Restart app, check for hung processes |

## File Structure

```
SharpCoreDb.Orchardcore/
├── Program.cs                    # App configuration
├── appsettings.json              # Database configuration
├── SharpCoreDbSetupHelper.cs     # Helper for setup
└── App_Data/
    └── Sites/
        └── Default/
            └── site.scdb         # ← Your database file
```

## Important Notes

✅ **Always use "Sqlite" as DatabaseProvider** (even though it uses SharpCoreDB)  
✅ **Table prefix "OC_" recommended** for OrchardCore tables  
✅ **Connection string is case-sensitive** on Linux  
✅ **Password is optional** but recommended  
✅ **App_Data/ must be writable** by the application  
✅ **Single file database** - easy to backup and move  

## Performance Tips

- First startup: ~3 seconds (schema creation)
- Subsequent startups: < 500ms
- Startup time is acceptable for development and testing

## Deployment Checklist

- [ ] Database file (`.scdb`) included or created on first run
- [ ] `App_Data/` directory exists and is writable
- [ ] Connection string in appsettings.json points to correct location
- [ ] Database provider is set to "Sqlite"
- [ ] Table prefix matches your schema
- [ ] Backup strategy in place for database file

## Code Example: Manual Configuration

If you need advanced control:

```csharp
// In Program.cs (before AddOrchardCms)
SharpCoreDbConfigurationExtensions.RegisterProviderFactory();

builder.Services.AddOrchardCms();
```

Then configure in appsettings.json - that's the recommended approach!

## Environment Variables

```bash
# Override connection string via environment variable
export OrchardCore__OrchardCore_Default__ConnectionString="Data Source=/data/site.scdb"
dotnet run
```

## Docker

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app
COPY . .

# Mount volume for database persistence
VOLUME ["/app/App_Data"]

EXPOSE 80
ENTRYPOINT ["dotnet", "SharpCoreDb.Orchardcore.dll"]
```

```bash
docker run -v database:/app/App_Data -p 80:80 sharpcoredb-orchardcore
```

## See Also

- **SHARPCOREDB_ORCHARDCORE_GUIDE.md** - Complete usage guide
- **TECHNICAL_ARCHITECTURE.md** - How the integration works
- **README.md** - Project overview
- **SharpCoreDb.Orchardcore/** - Example project

---

**That's it! SharpCoreDB + OrchardCore is simple and ready to use.** 🚀
