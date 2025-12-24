# ?? SharpCoreDB.EntityFrameworkCore - Visual Studio Quick Start

## Voor Visual Studio 2026 Gebruikers

### Stap 1: Build het Project

Druk op **F6** of ga naar `Build ? Build Solution`

### Stap 2: Maak NuGet Package

1. Ga naar **Solution Explorer**
2. **Right-click** op `SharpCoreDB.EntityFrameworkCore` project
3. Kies **Pack**

? **Klaar!** Het package staat in `bin\Release\SharpCoreDB.EntityFrameworkCore.1.0.0.nupkg`

### Stap 3: Publiceren naar NuGet.org

**Via Package Manager Console:**
```powershell
cd SharpCoreDB.EntityFrameworkCore
dotnet nuget push "bin\Release\SharpCoreDB.EntityFrameworkCore.1.0.0.nupkg" `
  --source https://api.nuget.org/v3/index.json `
  --api-key YOUR_API_KEY
```

---

## ?? Package Inhoud

Het package bevat automatisch:
- ? AnyCPU versie (fallback)
- ? Windows x64, ARM64
- ? Linux x64, ARM64
- ? macOS x64, ARM64 (Apple Silicon)
- ? Logo (SharpCoreDB.jpg)
- ? README.md
- ? Volledige documentation (USAGE_GUIDE.md)
- ? Dependency op SharpCoreDB package

---

## ?? Versienummer Wijzigen

Open `SharpCoreDB.EntityFrameworkCore.csproj`:
```xml
<Version>1.0.1</Version>  <!-- Update hier -->
```

**Let op:** Zorg dat de SharpCoreDB dependency versie klopt:
```xml
<PackageReference Include="SharpCoreDB" Version="1.0.0" />
```

---

## ?? Lokaal Testen

### 1. Test met lokale SharpCoreDB

Voor development gebruik je de `ProjectReference`:
```xml
<ProjectReference Include="..\SharpCoreDB\SharpCoreDB.csproj" />
```

### 2. Test met gepubliceerde SharpCoreDB

Voor NuGet package build:
```xml
<PackageReference Include="SharpCoreDB" Version="1.0.0" />
```

Schakel tussen beide met:
```xml
<PropertyGroup>
  <UseSharpCoreDBPackage>false</UseSharpCoreDBPackage> <!-- false = ProjectReference -->
</PropertyGroup>
```

### 3. Test het package lokaal

```powershell
# Voeg lokale source toe
dotnet nuget add source "D:\source\repos\MPCoreDeveloper\SharpCoreDB\SharpCoreDB.EntityFrameworkCore\bin\Release" --name LocalEFCore

# Maak test project
mkdir TestEFCore
cd TestEFCore
dotnet new console
dotnet add package SharpCoreDB.EntityFrameworkCore --version 1.0.0 --source LocalEFCore

# Test code
```

Voeg toe aan `Program.cs`:
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

// Test
using var db = new ShopContext();
db.Database.EnsureCreated();

db.Products.Add(new Product { Name = "Test", Price = 99.99m });
db.SaveChanges();

var products = db.Products.ToList();
Console.WriteLine($"Found {products.Count} products");
foreach (var p in products)
{
    Console.WriteLine($"  - {p.Name}: ${p.Price}");
}
```

Run: `dotnet run`

---

## ?? Publishing Checklist

- [ ] SharpCoreDB package is gepubliceerd op NuGet.org
- [ ] Version number is correct en > SharpCoreDB version
- [ ] README.md is up-to-date
- [ ] USAGE_GUIDE.md is compleet
- [ ] Logo (SharpCoreDB.jpg) exists
- [ ] Project builds zonder errors (F6)
- [ ] Pack succeeds (Right-click ? Pack)
- [ ] Dependency op SharpCoreDB is correct
- [ ] Getest lokaal met test project
- [ ] NuGet API key is ready

---

## ?? Build Workflow

### Development Mode
```xml
<!-- Use ProjectReference during development -->
<PropertyGroup>
  <UseSharpCoreDBPackage>false</UseSharpCoreDBPackage>
</PropertyGroup>
```

**Workflow:**
1. F5 ? Debug/test je code
2. Wijzigingen in SharpCoreDB worden direct opgepikt
3. Fast iteration cycle

### Release Mode
```xml
<!-- Use PackageReference for NuGet package -->
<PropertyGroup>
  <UseSharpCoreDBPackage>true</UseSharpCoreDBPackage>
</PropertyGroup>
```

**Workflow:**
1. Zet `UseSharpCoreDBPackage` naar `true`
2. F6 ? Build
3. Right-click ? Pack
4. Publish naar NuGet.org

---

## ?? Tips

**Tip 1: Automatic Multi-Platform Builds**
Bij Pack worden automatisch alle platforms gebouwd (win-x64, linux-x64, etc.)

**Tip 2: Dependency Versioning**
Update SharpCoreDB dependency bij breaking changes:
```xml
<PackageReference Include="SharpCoreDB" Version="2.0.0" />
```

**Tip 3: Symbol Package**
Het symbol package (.snupkg) wordt automatisch gemaakt voor debugging.

---

## ?? Documentatie Bestanden

| Bestand | Doel |
|---------|------|
| **README.md** | Korte intro en quick start (in NuGet package) |
| **USAGE_GUIDE.md** | Complete gebruikersgids met alle voorbeelden |
| **VISUAL_STUDIO_GUIDE.md** | Deze gids - VS workflow |

---

## ?? Troubleshooting

### "Project reference not found"
**Oplossing:** Check dat `SharpCoreDB.csproj` bestaat in parent folder:
```xml
<ProjectReference Include="..\SharpCoreDB\SharpCoreDB.csproj" />
```

### "SharpCoreDB package not found"
**Oplossing:** Zorg dat SharpCoreDB eerst naar NuGet.org is gepubliceerd, of gebruik `UseSharpCoreDBPackage=false`.

### "Version conflict"
**Oplossing:** 
- Clean solution
- Rebuild
- Check dependency versions match

---

## ?? Publicatie Workflow

```
1. Publish SharpCoreDB naar NuGet.org
   ?
2. Update SharpCoreDB dependency version in .csproj
   ?
3. Set UseSharpCoreDBPackage = true
   ?
4. Build (F6)
   ?
5. Pack (Right-click)
   ?
6. Test lokaal
   ?
7. Push naar NuGet.org
   ?
8. Done! ??
```

---

**Veel succes met SharpCoreDB.EntityFrameworkCore!** ??

Zie **USAGE_GUIDE.md** voor complete code voorbeelden en best practices.
