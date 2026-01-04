# ? SharpCoreDB.EntityFrameworkCore - NuGet Setup Compleet

## ?? Overzicht

SharpCoreDB.EntityFrameworkCore is nu volledig geconfigureerd voor NuGet publicatie met dezelfde multi-platform ondersteuning als SharpCoreDB.

---

## ?? Package Details

### Package Naam
`SharpCoreDB.EntityFrameworkCore`

### Versie
`1.0.0`

### Dependencies
- `SharpCoreDB` >= 1.0.0
- `Microsoft.EntityFrameworkCore` 10.0.1
- `Microsoft.EntityFrameworkCore.Relational` 10.0.1

### Platform Support
Automatisch voor alle platforms:
- ? Windows x64, ARM64
- ? Linux x64, ARM64
- ? macOS x64, ARM64 (Apple Silicon)
- ? AnyCPU fallback

---

## ?? Quick Start (Visual Studio)

### Voor Development (met ProjectReference)

```
1. Open SharpCoreDB.sln in Visual Studio 2026
2. F6 ? Build Solution
3. F5 ? Debug/Run
```

**Configuratie:** `UseSharpCoreDBPackage = false` (default)

### Voor NuGet Package (met PackageReference)

```
1. Zet in .csproj: <UseSharpCoreDBPackage>true</UseSharpCoreDBPackage>
2. F6 ? Build
3. Right-click project ? Pack
4. Package in bin\Release\
```

---

## ?? Documentatie

| Bestand | Doel | Locatie |
|---------|------|---------|
| **README.md** | Quick start, zit in NuGet package | EntityFrameworkCore/ |
| **USAGE_GUIDE.md** | Complete usage guide met alle voorbeelden | EntityFrameworkCore/ |
| **VISUAL_STUDIO_GUIDE.md** | VS 2026 workflow guide | EntityFrameworkCore/ |
| **EFCORE_SETUP_COMPLETE.md** | Dit overzicht | EntityFrameworkCore/ |

---

## ?? Voorbeeld Code

### Minimaal Setup

```csharp
using Microsoft.EntityFrameworkCore;
using SharpCoreDB.EntityFrameworkCore;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}

public class ShopContext : DbContext
{
    public DbSet<Product> Products { get; set; }
    
    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseSharpCoreDB("Data Source=shop.db");
    }
}

// Gebruik
using var db = new ShopContext();
db.Database.EnsureCreated();

db.Products.Add(new Product { Name = "Laptop", Price = 999.99m });
await db.SaveChangesAsync();

var products = await db.Products.ToListAsync();
```

### Met Encryption

```csharp
protected override void OnConfiguring(DbContextOptionsBuilder options)
{
    options.UseSharpCoreDB(
        "Data Source=shop.db;Encryption=true;Password=SecurePass123",
        sharpCoreOptions => 
        {
            sharpCoreOptions.SetCacheSize(100); // MB
            sharpCoreOptions.CommandTimeout(30);
            sharpCoreOptions.EnableRetryOnFailure(3);
        });
}
```

### ASP.NET Core Setup

```csharp
// Program.cs
builder.Services.AddDbContext<ShopContext>(options =>
    options.UseSharpCoreDB(
        builder.Configuration.GetConnectionString("DefaultConnection")));

// appsettings.json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=shop.db;Encryption=true;Password=SecurePass"
  }
}
```

---

## ?? Project Configuration

### Development vs Production

```xml
<PropertyGroup>
  <!-- Development: gebruik ProjectReference -->
  <UseSharpCoreDBPackage>false</UseSharpCoreDBPackage>
  
  <!-- Production: gebruik PackageReference -->
  <!-- <UseSharpCoreDBPackage>true</UseSharpCoreDBPackage> -->
</PropertyGroup>
```

### Multi-Platform Builds

```xml
<PropertyGroup>
  <!-- Automatisch bij Pack -->
  <RuntimeIdentifiers>win-x64;win-arm64;linux-x64;linux-arm64;osx-x64;osx-arm64</RuntimeIdentifiers>
</PropertyGroup>

<Target Name="BuildAllRuntimesForPack" BeforeTargets="Pack">
  <!-- Bouwt automatisch alle platforms -->
</Target>
```

---

## ?? Publishing Workflow

### Stap 1: Zorg dat SharpCoreDB gepubliceerd is

```powershell
cd ..\SharpCoreDB
# Build en publiceer SharpCoreDB eerst
```

### Stap 2: Update Dependency

```xml
<PackageReference Include="SharpCoreDB" Version="1.0.0" />
```

### Stap 3: Schakel naar Package Mode

```xml
<UseSharpCoreDBPackage>true</UseSharpCoreDBPackage>
```

### Stap 4: Build & Pack

```
Visual Studio:
1. F6 (Build)
2. Right-click project ? Pack
```

### Stap 5: Test Lokaal

```powershell
dotnet nuget add source "D:\source\repos\MPCoreDeveloper\SharpCoreDB\SharpCoreDB.EntityFrameworkCore\bin\Release" --name LocalEFCore

mkdir TestApp
cd TestApp
dotnet new console
dotnet add package SharpCoreDB.EntityFrameworkCore --version 1.0.0 --source LocalEFCore
```

### Stap 6: Publiceer

```powershell
dotnet nuget push "bin\Release\SharpCoreDB.EntityFrameworkCore.1.0.0.nupkg" `
  --source https://api.nuget.org/v3/index.json `
  --api-key YOUR_API_KEY
```

---

## ?? Package Structuur

```
SharpCoreDB.EntityFrameworkCore.1.0.0.nupkg
??? lib/net10.0/
?   ??? SharpCoreDB.EntityFrameworkCore.dll (AnyCPU)
?   ??? SharpCoreDB.EntityFrameworkCore.xml
??? runtimes/
?   ??? win-x64/lib/net10.0/
?   ??? win-arm64/lib/net10.0/
?   ??? linux-x64/lib/net10.0/
?   ??? linux-arm64/lib/net10.0/
?   ??? osx-x64/lib/net10.0/
?   ??? osx-arm64/lib/net10.0/
??? SharpCoreDB.jpg (logo)
??? README.md
??? dependencies:
    ??? SharpCoreDB >= 1.0.0
    ??? Microsoft.EntityFrameworkCore 10.0.1
    ??? Microsoft.EntityFrameworkCore.Relational 10.0.1
```

---

## ? Features

### Entity Framework Core Features
- ? LINQ queries
- ? Change tracking
- ? Migrations
- ? Relationships (1-to-many, many-to-many)
- ? Lazy/Eager loading
- ? Raw SQL support
- ? Transactions
- ? Compiled queries
- ? Query splitting
- ? Global query filters

### SharpCoreDB Features
- ? AES-256-GCM encryption
- ? File-based database
- ? Platform optimizations (AVX2/NEON)
- ? Cross-platform support
- ? Zero configuration
- ? Lightweight

---

## ?? Use Cases

### Perfect For

1. **Desktop Applications**
   - WPF, WinForms, Avalonia
   - Encrypted local database
   - Full EF Core ORM support

2. **Web Applications**
   - ASP.NET Core API
   - Blazor Server/WASM
   - Embedded database

3. **Mobile Apps**
   - MAUI, Xamarin
   - Encrypted data storage
   - Offline-first apps

4. **IoT/Embedded**
   - Raspberry Pi
   - Edge devices
   - Industrial automation

---

## ?? Version Management

### When to Update

| Change | Action |
|--------|--------|
| SharpCoreDB bug fix | Patch version (1.0.1) |
| New EF Core feature | Minor version (1.1.0) |
| Breaking API change | Major version (2.0.0) |

### Dependency Rules

```xml
<!-- Always specify minimum SharpCoreDB version -->
<PackageReference Include="SharpCoreDB" Version="1.0.0" />

<!-- For breaking changes, use exact version -->
<PackageReference Include="SharpCoreDB" Version="[2.0.0]" />
```

---

## ?? Testing Checklist

- [ ] Build succeeds (F6)
- [ ] Pack succeeds (Right-click ? Pack)
- [ ] Package contains all platforms
- [ ] Logo is included
- [ ] README is up-to-date
- [ ] Dependencies are correct
- [ ] Tested with sample project
- [ ] Documentation is complete
- [ ] Version number is correct

---

## ?? Common Issues

### "Could not load file or assembly 'SharpCoreDB'"

**Oplossing:**
```xml
<!-- Zorg dat SharpCoreDB dependency correct is -->
<PackageReference Include="SharpCoreDB" Version="1.0.0" />
```

### "Project reference not found"

**Oplossing:**
```xml
<!-- Check path naar SharpCoreDB project -->
<ProjectReference Include="..\SharpCoreDB\SharpCoreDB.csproj" />
```

### "Package already exists with version X"

**Oplossing:**
```xml
<!-- Verhoog version number -->
<Version>1.0.1</Version>
```

---

## ?? Vergelijk met Andere Providers

| Feature | SharpCoreDB.EFCore | EFCore.Sqlite | EFCore.SqlServer |
|---------|-------------------|---------------|------------------|
| Encryption | ? Built-in AES-256 | ? | ? |
| Setup | ? Zero config | ? | ? Server required |
| Mobile | ? | ? | ? |
| ARM64 optimized | ? NEON | ? | ? |
| Cross-platform | ? | ? | ? |
| File-based | ? | ? | ? |

---

## ?? Klaar voor Productie

SharpCoreDB.EntityFrameworkCore is nu:
- ? **Geconfigureerd** voor multi-platform builds
- ? **Gedocumenteerd** met complete guides
- ? **Getest** en compileert succesvol
- ? **Klaar** voor publicatie naar NuGet.org

---

## ?? Volgende Stappen

1. ? **Publiceer SharpCoreDB** (als nog niet gedaan)
2. ? **Build SharpCoreDB.EntityFrameworkCore** (F6)
3. ? **Pack SharpCoreDB.EntityFrameworkCore** (Right-click ? Pack)
4. ? **Test lokaal** met sample project
5. ? **Publiceer naar NuGet.org**
6. ?? **Done!**

---

**Veel succes met je Entity Framework Core provider!** ??

Voor vragen of problemen:
- **Documentation**: Zie USAGE_GUIDE.md
- **VS Workflow**: Zie VISUAL_STUDIO_GUIDE.md
- **GitHub**: https://github.com/MPCoreDeveloper/SharpCoreDB/issues
