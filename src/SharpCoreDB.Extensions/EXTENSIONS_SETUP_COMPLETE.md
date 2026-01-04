# ? SharpCoreDB.Extensions - NuGet Setup Compleet

## ?? Overzicht

SharpCoreDB.Extensions is nu volledig geconfigureerd voor NuGet publicatie met:
- ? **Dapper Integration** - Voor developers die de simpliciteit van Dapper prefereren
- ? **ASP.NET Core Health Checks** - Voor production monitoring
- ? **Multi-platform support** - Alle SharpCoreDB platforms
- ? **GitHub Documentation** - Complete guides via links

---

## ?? Package Details

### Package Naam
`SharpCoreDB.Extensions`

### Versie
`1.0.0`

### Dependencies
- `SharpCoreDB` >= 1.0.0
- `Dapper` >= 2.1.66
- `Microsoft.Extensions.Diagnostics.HealthChecks` >= 10.0.1

### Platform Support
- Windows x64, ARM64
- Linux x64, ARM64
- macOS x64, ARM64 (Apple Silicon)
- AnyCPU fallback

---

## ?? Quick Examples

### Dapper Query

```csharp
using SharpCoreDB.Extensions.Dapper;
using Dapper;

using var connection = new SharpCoreDBConnection(
    "Data Source=app.db;Encryption=true;Password=SecurePass");
connection.Open();

var products = await connection.QueryAsync<Product>(
    "SELECT * FROM Products WHERE Price > @MinPrice",
    new { MinPrice = 100 });
```

### Health Check

```csharp
// Program.cs
builder.Services
    .AddHealthChecks()
    .AddSharpCoreDB(
        connectionString: builder.Configuration.GetConnectionString("DefaultConnection"),
        name: "sharpcoredb");

app.MapHealthChecks("/health");
```

---

## ?? Documentatie Strategie

### In NuGet Package (README.md)
- ? Quick start voorbeelden
- ? Basis Dapper usage
- ? Basis Health Check setup
- ? **Links naar volledige documentatie op GitHub**

### Op GitHub (USAGE_GUIDE.md)
- ? Complete Dapper scenarios
- ? Advanced health checks
- ? Performance optimization
- ? Best practices
- ? Troubleshooting

**Voordeel:** 
- README blijft compact (snel inzicht)
- Complete docs altijd up-to-date op GitHub
- Geen verouderde docs in oude NuGet versies

---

## ?? Documentation Links

Users zien in het NuGet package README:

```markdown
## ?? Complete Documentation

Voor uitgebreide documentatie en voorbeelden:

### ?? [Complete Usage Guide op GitHub](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/SharpCoreDB.Extensions/USAGE_GUIDE.md)
```

**Toegang vanuit:**
1. NuGet.org package page
2. Visual Studio package manager (README tab)
3. `dotnet add package` ? README wordt getoond
4. IDE's met NuGet integration

---

## ?? Visual Studio Workflow

### Development Mode

```
1. F6 ? Build
2. F5 ? Debug/Test
```

**Configuratie:** Gebruik `ProjectReference` (default)

### Release Mode

```
1. Zet UseSharpCoreDBPackage = true
2. F6 ? Build
3. Right-click ? Pack
4. Package in bin\Release\
```

---

## ?? Package Structuur

```
SharpCoreDB.Extensions.1.0.0.nupkg
??? lib/net10.0/
?   ??? SharpCoreDB.Extensions.dll
?   ??? SharpCoreDB.Extensions.xml
??? runtimes/
?   ??? win-x64/lib/net10.0/
?   ??? win-arm64/lib/net10.0/
?   ??? linux-x64/lib/net10.0/
?   ??? linux-arm64/lib/net10.0/
?   ??? osx-x64/lib/net10.0/
?   ??? osx-arm64/lib/net10.0/
??? SharpCoreDB.jpg (logo)
??? README.md (met GitHub links)
??? dependencies:
    ??? SharpCoreDB >= 1.0.0
    ??? Dapper >= 2.1.66
    ??? Microsoft.Extensions.Diagnostics.HealthChecks >= 10.0.1
```

---

## ?? Features Overzicht

### Dapper Integration
- ? Full Dapper support op SharpCoreDB
- ? Connection pooling
- ? Transaction support
- ? Multi-mapping
- ? Bulk operations
- ? Stored procedures

### Health Checks
- ? ASP.NET Core integration
- ? Custom health queries
- ? Multiple database checks
- ? Configurable timeouts
- ? Degraded status support
- ? Health Check UI compatible

---

## ?? Publishing Checklist

- [ ] SharpCoreDB is gepubliceerd
- [ ] Version number correct
- [ ] README.md met GitHub links
- [ ] USAGE_GUIDE.md compleet op GitHub
- [ ] Logo included
- [ ] Dependencies correct
- [ ] Build succeeds (F6)
- [ ] Pack succeeds
- [ ] Tested lokaal

---

## ?? Publishing Workflow

```
1. Publish SharpCoreDB
   ?
2. Update dependency version
   ?
3. Set UseSharpCoreDBPackage = true
   ?
4. F6 (Build)
   ?
5. Right-click ? Pack
   ?
6. Test lokaal
   ?
7. Push naar NuGet.org
   ?
8. Commit USAGE_GUIDE.md naar GitHub
   ?
9. Done! ??
```

---

## ?? Use Cases

### Perfect For

**1. Micro-ORM Fans**
- Developers die Dapper's simpliciteit prefereren
- Direct SQL control gewenst
- Performance critical applications

**2. ASP.NET Core APIs**
- RESTful services met health monitoring
- Kubernetes health/ready probes
- Azure App Service health checks

**3. Hybrid Approaches**
- Dapper voor queries + EF Core voor complex domain logic
- Beste van beide werelden

---

## ?? Comparison

| Feature | SharpCoreDB.Extensions | SharpCoreDB.EFCore |
|---------|----------------------|-------------------|
| **Approach** | Micro-ORM (Dapper) | Full ORM |
| **Setup** | Minimal | More setup |
| **Performance** | ? Fastest | Fast |
| **Learning Curve** | Easy | Moderate |
| **SQL Control** | Full | Abstracted |
| **Migrations** | Manual | Automated |
| **Best For** | APIs, simple CRUD | Complex domains |

**Tip:** Je kunt beide packages gebruiken in hetzelfde project!

---

## ?? Documentation Strategy

### Waarom GitHub Links?

**Voordelen:**
1. ? **Altijd actueel** - …Èn bron van waarheid
2. ? **Versiecontrole** - Git history van docs
3. ? **Community** - Issues, PRs voor doc verbeteringen
4. ? **Searchable** - GitHub search indexeert docs
5. ? **Markdown rendering** - Mooie formatting
6. ? **Geen limiet** - Onbeperkte doc grootte

**Package README:**
- Kort en krachtig
- Quick start voorbeelden
- Links naar volledige docs

**GitHub USAGE_GUIDE:**
- Complete voorbeelden
- Advanced scenarios
- Best practices
- Troubleshooting

---

## ?? Resultaat

### Users zien in NuGet:
```
SharpCoreDB.Extensions
??? Quick start voorbeelden
??? Basis gebruik
??? ?? Link naar Complete Guide op GitHub
```

### Users vinden op GitHub:
```
SharpCoreDB/SharpCoreDB.Extensions/
??? README.md (sync met NuGet)
??? USAGE_GUIDE.md (uitgebreid)
??? Examples/ (code samples)
```

---

## ?? Volgende Stappen

1. ? **Build & Test** (F6)
2. ? **Pack** (Right-click ? Pack)
3. ? **Publish** naar NuGet.org
4. ? **Commit** USAGE_GUIDE.md naar GitHub
5. ? **Verify** links werken op NuGet.org
6. ?? **Done!**

---

**SharpCoreDB.Extensions is klaar voor de community!** ??

Gebruikers krijgen:
- Snelle start in NuGet README
- Uitgebreide docs via GitHub link
- Altijd actuele documentatie
- Best of both worlds!
