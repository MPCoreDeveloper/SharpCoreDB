# ?? SharpCoreDB NuGet Packages - Complete Setup

Alle drie de SharpCoreDB packages zijn nu klaar voor publicatie naar NuGet.org!

---

## ?? Package Overzicht

| Package | Versie | Doel | Status |
|---------|--------|------|--------|
| **SharpCoreDB** | 1.0.0 | Core database engine | ? Klaar |
| **SharpCoreDB.EntityFrameworkCore** | 1.0.0 | EF Core provider | ? Klaar |
| **SharpCoreDB.Extensions** | 1.0.0 | Dapper + Health Checks | ? Klaar |

---

## ?? SharpCoreDB (Core)

### Features
- ? Encrypted file-based database (AES-256-GCM)
- ? SQL query support
- ? Platform optimizations (AVX2/NEON)
- ? Multi-platform (Desktop, Mobile, IoT)

### Quick Example
```csharp
var db = new SharpCoreDB("Data Source=app.db;Encryption=true;Password=SecurePass");
db.Execute("CREATE TABLE Products (Id INT, Name TEXT, Price REAL)");
db.Execute("INSERT INTO Products VALUES (1, 'Laptop', 999.99)");
var results = db.Query("SELECT * FROM Products WHERE Price > 500");
```

### Documentation
- **README.md** - In package met quick start
- **PUBLISH_QUICK_START.md** - Publishing guide
- **VISUAL_STUDIO_GUIDE.md** - VS workflow
- **PLATFORM_SUPPORT.md** - Alle platforms
- **BUILD.md** - Gedetailleerde build instructies

### Publishing
```powershell
# Visual Studio
F6 ? Build
Right-click ? Pack

# Command line
dotnet pack -c Release
dotnet nuget push bin\Release\SharpCoreDB.1.0.0.nupkg --source https://api.nuget.org/v3/index.json --api-key YOUR_KEY
```

---

## ??? SharpCoreDB.EntityFrameworkCore

### Features
- ? Full Entity Framework Core support
- ? LINQ queries
- ? Migrations
- ? Relationships (1-to-many, many-to-many)
- ? Change tracking

### Quick Example
```csharp
public class Product
{
    public int Id { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
}

public class ShopContext : DbContext
{
    public DbSet<Product> Products { get; set; }
    
    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseSharpCoreDB("Data Source=shop.db;Encryption=true;Password=SecurePass");
    }
}

// Usage
using var db = new ShopContext();
db.Products.Add(new Product { Name = "Laptop", Price = 999.99m });
await db.SaveChangesAsync();

var products = await db.Products.Where(p => p.Price > 500).ToListAsync();
```

### Documentation Strategy
- **README.md** - Quick start in package + **Link naar GitHub**
- **USAGE_GUIDE.md** - Complete guide op GitHub
- **VISUAL_STUDIO_GUIDE.md** - VS workflow
- **EFCORE_SETUP_COMPLETE.md** - Setup overzicht

### Dependencies
- SharpCoreDB >= 1.0.0
- Microsoft.EntityFrameworkCore >= 10.0.1
- Microsoft.EntityFrameworkCore.Relational >= 10.0.1

### Publishing
```powershell
# Zorg dat SharpCoreDB eerst gepubliceerd is!
# Dan:
F6 ? Build
Right-click ? Pack
dotnet nuget push bin\Release\SharpCoreDB.EntityFrameworkCore.1.0.0.nupkg --source https://api.nuget.org/v3/index.json --api-key YOUR_KEY
```

---

## ?? SharpCoreDB.Extensions

### Features
- ? Dapper integration
- ? ASP.NET Core health checks
- ? Connection pooling
- ? Transaction support
- ? Bulk operations

### Quick Example - Dapper
```csharp
using SharpCoreDB.Extensions.Dapper;
using Dapper;

using var connection = new SharpCoreDBConnection(
    "Data Source=app.db;Encryption=true;Password=SecurePass");
connection.Open();

var products = await connection.QueryAsync<Product>(
    "SELECT * FROM Products WHERE Price > @MinPrice",
    new { MinPrice = 100 });

await connection.ExecuteAsync(
    "INSERT INTO Products (Name, Price) VALUES (@Name, @Price)",
    new { Name = "Mouse", Price = 29.99m });
```

### Quick Example - Health Checks
```csharp
// Program.cs
builder.Services
    .AddHealthChecks()
    .AddSharpCoreDB(
        connectionString: builder.Configuration.GetConnectionString("DefaultConnection"),
        name: "sharpcoredb");

app.MapHealthChecks("/health");
```

### Documentation Strategy
- **README.md** - Quick start + **Link naar GitHub voor complete guide**
- **USAGE_GUIDE.md** - Uitgebreide guide op GitHub
- **EXTENSIONS_SETUP_COMPLETE.md** - Setup overzicht

### Dependencies
- SharpCoreDB >= 1.0.0
- Dapper >= 2.1.66
- Microsoft.Extensions.Diagnostics.HealthChecks >= 10.0.1

### Publishing
```powershell
# Zorg dat SharpCoreDB eerst gepubliceerd is!
F6 ? Build
Right-click ? Pack
dotnet nuget push bin\Release\SharpCoreDB.Extensions.1.0.0.nupkg --source https://api.nuget.org/v3/index.json --api-key YOUR_KEY
```

---

## ?? Documentatie Strategie

### In NuGet Packages (README.md)
? **Compact en overzichtelijk**
- Quick start voorbeelden
- Basis features
- Installatie instructies
- **Links naar GitHub voor complete docs**

### Op GitHub
? **Uitgebreid en altijd actueel**
- Complete usage guides
- Advanced scenarios
- Best practices
- Troubleshooting
- Code voorbeelden

### Voordelen
1. ? Package README blijft compact (snel inzicht)
2. ? Complete docs altijd up-to-date
3. ? Geen verouderde docs in oude NuGet versies
4. ? Community kan docs verbeteren via PRs
5. ? Searchable op GitHub
6. ? Mooie markdown rendering

---

## ?? Publishing Volgorde

### Stap 1: SharpCoreDB (Core)
```
? F6 ? Build
? Right-click ? Pack
? Test lokaal
? dotnet nuget push
? Verify op NuGet.org
```

### Stap 2: SharpCoreDB.EntityFrameworkCore
```
? Update dependency: <PackageReference Include="SharpCoreDB" Version="1.0.0" />
? Set UseSharpCoreDBPackage = true
? F6 ? Build
? Right-click ? Pack
? Test lokaal
? dotnet nuget push
? Commit USAGE_GUIDE.md naar GitHub
```

### Stap 3: SharpCoreDB.Extensions
```
? Update dependency: <PackageReference Include="SharpCoreDB" Version="1.0.0" />
? Set UseSharpCoreDBPackage = true
? F6 ? Build
? Right-click ? Pack
? Test lokaal
? dotnet nuget push
? Commit USAGE_GUIDE.md naar GitHub
```

---

## ? Checklist Voordat Je Publiceert

### SharpCoreDB
- [ ] Version number correct (1.0.0)
- [ ] Logo (SharpCoreDB.jpg) aanwezig
- [ ] README.md up-to-date
- [ ] Platform builds werken
- [ ] Build succeeds (F6)
- [ ] Pack succeeds
- [ ] Tested lokaal

### SharpCoreDB.EntityFrameworkCore
- [ ] SharpCoreDB dependency correct (>= 1.0.0)
- [ ] UseSharpCoreDBPackage = true
- [ ] Version number correct
- [ ] README.md met GitHub links
- [ ] USAGE_GUIDE.md compleet
- [ ] Build succeeds
- [ ] Pack succeeds
- [ ] Tested met sample project

### SharpCoreDB.Extensions
- [ ] SharpCoreDB dependency correct (>= 1.0.0)
- [ ] UseSharpCoreDBPackage = true
- [ ] Version number correct
- [ ] README.md met GitHub links
- [ ] USAGE_GUIDE.md compleet
- [ ] Build succeeds
- [ ] Pack succeeds
- [ ] Tested met sample project

---

## ?? User Journey

### Developer wil encrypted database
```
npm/dotnet add package SharpCoreDB
? Leest README in package
? Volgt quick start
? Werkt direct!
```

### Developer wil EF Core gebruiken
```
dotnet add package SharpCoreDB.EntityFrameworkCore
? Leest README in package
? Ziet: "Voor complete guide, klik hier (GitHub link)"
? Leest USAGE_GUIDE.md op GitHub
? Vindt alle advanced scenarios
```

### Developer wil Dapper + Health Checks
```
dotnet add package SharpCoreDB.Extensions
? Leest README in package
? Quick start voor Dapper
? Quick start voor Health Checks
? Voor advanced: klik link naar GitHub
? USAGE_GUIDE.md heeft alles
```

---

## ?? Platform Support (Alle Packages)

| Platform | Architecture | Auto-build bij Pack |
|----------|--------------|-------------------|
| Windows | x64 | ? |
| Windows | ARM64 | ? |
| Linux | x64 | ? |
| Linux | ARM64 | ? |
| macOS | x64 (Intel) | ? |
| macOS | ARM64 (Apple Silicon) | ? |
| AnyCPU | Fallback | ? |

**Optioneel (uncomment in .csproj):**
- Android ARM64, x64
- iOS ARM64
- IoT Linux ARM32

---

## ?? Key Features Across Packages

### SharpCoreDB (Core)
- Direct SQL execution
- Maximum control
- Lightweight
- Perfect voor: Desktop apps, services, embedded

### SharpCoreDB.EntityFrameworkCore
- Full ORM features
- LINQ queries
- Migrations
- Perfect voor: Complex domains, web apps, CRUD APIs

### SharpCoreDB.Extensions
- Micro-ORM (Dapper)
- Health monitoring
- Best van beide werelden
- Perfect voor: APIs, microservices, hybrid approaches

---

## ?? Resultaat

### Voor Gebruikers
- ? **Keuze** - Core, EF Core, of Dapper
- ? **Documentatie** - Compact in package + uitgebreid op GitHub
- ? **Performance** - Platform optimizations (AVX2/NEON)
- ? **Security** - Built-in encryption
- ? **Cross-platform** - Overal werken
- ? **Modern** - .NET 10, C# 14

### Voor Jou
- ? **Visual Studio workflow** - F6 + Right-click ? Pack
- ? **No scripts needed** - Alles geïntegreerd in .csproj
- ? **Maintainable docs** - Centraal op GitHub
- ? **Community ready** - GitHub PRs voor docs

---

## ?? Final Steps

```bash
# 1. Publish SharpCoreDB
cd SharpCoreDB
dotnet pack -c Release
dotnet nuget push bin\Release\SharpCoreDB.1.0.0.nupkg --api-key YOUR_KEY

# 2. Publish SharpCoreDB.EntityFrameworkCore
cd ..\SharpCoreDB.EntityFrameworkCore
# Update dependency, set UseSharpCoreDBPackage=true
dotnet pack -c Release
dotnet nuget push bin\Release\SharpCoreDB.EntityFrameworkCore.1.0.0.nupkg --api-key YOUR_KEY

# 3. Publish SharpCoreDB.Extensions
cd ..\SharpCoreDB.Extensions
# Update dependency, set UseSharpCoreDBPackage=true
dotnet pack -c Release
dotnet nuget push bin\Release\SharpCoreDB.Extensions.1.0.0.nupkg --api-key YOUR_KEY

# 4. Commit documentation naar GitHub
git add SharpCoreDB.EntityFrameworkCore/USAGE_GUIDE.md
git add SharpCoreDB.Extensions/USAGE_GUIDE.md
git commit -m "Add complete usage guides"
git push

# 5. Verify op NuGet.org
# Check dat links in README's naar GitHub werken
```

---

## ?? Gefeliciteerd!

Je hebt nu een complete NuGet package familie voor SharpCoreDB:

1. **SharpCoreDB** - Core engine
2. **SharpCoreDB.EntityFrameworkCore** - Full ORM
3. **SharpCoreDB.Extensions** - Dapper + Health Checks

Alle met:
- ? Multi-platform support
- ? Platform optimizations
- ? Visual Studio integration
- ? Complete documentatie (in package + GitHub)
- ? Professional setup

**Klaar voor publicatie!** ??

---

## ?? Support After Publishing

- **NuGet**: https://www.nuget.org/profiles/MPCoreDeveloper
- **GitHub**: https://github.com/MPCoreDeveloper/SharpCoreDB
- **Issues**: https://github.com/MPCoreDeveloper/SharpCoreDB/issues
- **Discussions**: https://github.com/MPCoreDeveloper/SharpCoreDB/discussions

---

**Happy publishing!** ??
