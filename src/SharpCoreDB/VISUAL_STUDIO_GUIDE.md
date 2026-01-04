# SharpCoreDB - Visual Studio 2026 Workflow

## 🚀 Quick Start (Visual Studio)

### 1. Build het project
```
Build → Build Solution (F6)
of
Build → Build SharpCoreDB
```

### 2. Maak NuGet Package
```
Right-click op SharpCoreDB project → Pack
```

**Klaar!** Het NuGet package met alle platform-specifieke assemblies staat in:
```
D:\source\repos\MPCoreDeveloper\SharpCoreDB\SharpCoreDB\bin\Release\SharpCoreDB.1.0.0.nupkg
```

## 📦 Wat gebeurt er bij Pack?

Visual Studio bouwt automatisch:
- ✅ AnyCPU (fallback)
- ✅ Windows x64
- ✅ Windows ARM64
- ✅ Linux x64
- ✅ Linux ARM64
- ✅ macOS x64
- ✅ macOS ARM64

Alle assemblies worden automatisch in het NuGet package gestopt in de juiste `runtimes/` folders.

## 🔧 Configuratie Aanpassen

### Versienummer wijzigen
Open `SharpCoreDB.csproj` en pas aan:
```xml
<Version>1.0.0</Version>  <!-- Wijzig hier -->
```

### Mobile platforms toevoegen (Android/iOS)
Uncomment in `SharpCoreDB.csproj`:
```xml
<!-- Android platforms (uncomment if building mobile) -->
<PropertyGroup Condition="'$(RuntimeIdentifier)' == 'android-arm64' OR '$(RuntimeIdentifier)' == 'android-x64'">
  <DefineConstants>$(DefineConstants);ANDROID</DefineConstants>
</PropertyGroup>
```

En voeg toe aan `<RuntimeIdentifiers>`:
```xml
<RuntimeIdentifiers>win-x64;win-arm64;linux-x64;linux-arm64;osx-x64;osx-arm64;android-arm64;ios-arm64</RuntimeIdentifiers>
```

## 📤 Publiceren naar NuGet.org

### Via Visual Studio
1. Tools → NuGet Package Manager → Package Manager Console
2. Run:
```powershell
dotnet nuget push "bin\Release\SharpCoreDB.1.0.0.nupkg" `
  --source https://api.nuget.org/v3/index.json `
  --api-key YOUR_API_KEY
```

### Via Command Line
Open Developer PowerShell:
```powershell
cd D:\source\repos\MPCoreDeveloper\SharpCoreDB\SharpCoreDB
dotnet nuget push "bin\Release\SharpCoreDB.1.0.0.nupkg" `
  --source https://api.nuget.org/v3/index.json `
  --api-key YOUR_API_KEY
```

### API Key verkrijgen
1. Ga naar https://www.nuget.org/account/apikeys
2. Create nieuwe key voor SharpCoreDB
3. Gebruik de key in het push commando

## 🧪 Lokaal Testen

### Package testen zonder te publiceren

1. **Voeg lokale source toe:**
```powershell
dotnet nuget add source "D:\source\repos\MPCoreDeveloper\SharpCoreDB\SharpCoreDB\bin\Release" `
  --name LocalSharpCoreDB
```

2. **Maak test project:**
```powershell
mkdir TestApp
cd TestApp
dotnet new console
dotnet add package SharpCoreDB --version 1.0.0 --source LocalSharpCoreDB
```

3. **Test de package:**
```csharp
using SharpCoreDB.Platform;

Console.WriteLine(PlatformOptimizations.GetPlatformInfo());
```

## 🎯 Build Configuraties

### Release Build (aanbevolen voor NuGet)
```
Build → Configuration Manager → Release
```
Daarna: Right-click → Pack

### Debug Build
```
Build → Configuration Manager → Debug
```
Daarna: Right-click → Pack

## 📊 Package Inhoud Verifiëren

### Via NuGet Package Explorer
1. Download: https://github.com/NuGetPackageExplorer/NuGetPackageExplorer
2. Open `.nupkg` file
3. Controleer:
   - ✅ Logo (SharpCoreDB.jpg)
   - ✅ README.md
   - ✅ `lib/net10.0/SharpCoreDB.dll` (AnyCPU)
   - ✅ `runtimes/win-x64/lib/net10.0/SharpCoreDB.dll`
   - ✅ `runtimes/win-arm64/lib/net10.0/SharpCoreDB.dll`
   - ✅ etc.

### Via Command Line
```powershell
# Extract package contents
Expand-Archive -Path SharpCoreDB.1.0.0.nupkg -DestinationPath extracted

# View structure
tree extracted /F
```

## 🔄 GitHub Integration (Optioneel)

Als je automatische builds wilt bij push naar GitHub:

1. Push je code naar GitHub
2. GitHub Actions (in `.github/workflows/nuget-build.yml`) bouwt automatisch
3. Bij tag `v1.0.0` wordt automatisch gepubliceerd naar NuGet.org

**Setup:**
```bash
# Create tag
git tag -a v1.0.0 -m "Release version 1.0.0"
git push origin v1.0.0

# GitHub Actions neemt het over!
```

Voeg je NuGet API key toe aan GitHub:
- Settings → Secrets → New repository secret
- Name: `NUGET_API_KEY`
- Value: (je API key)

## ⚡ Snelle Commands

| Actie | Command / Shortcut |
|-------|-------------------|
| Build | `F6` of `Ctrl+Shift+B` |
| Clean | `Build → Clean Solution` |
| Rebuild | `Build → Rebuild Solution` |
| Pack | `Right-click project → Pack` |
| Restore NuGet | `Right-click solution → Restore NuGet Packages` |

## 🐛 Troubleshooting

### "Pack failed - runtime assemblies not found"
**Oplossing:** Build eerst het project (F6), dan Pack.

### "Version conflict"
**Oplossing:** Clean Solution, dan Rebuild, dan Pack.

### "Assembly missing for linux-x64"
**Oplossing:** Dit is normaal op Windows. De build probeert alle platforms maar sommige kunnen falen. Het package bevat de succesvolle builds.

### Package te groot
**Oplossing:** Verwijder debug symbols:
```xml
<IncludeSymbols>false</IncludeSymbols>
```

## 📝 Best Practices

1. **Versienummering:** Gebruik [SemVer](https://semver.org/)
   - Major.Minor.Patch (bijv. 1.0.0)
   - Breaking changes → Major
   - Features → Minor  
   - Bugfixes → Patch

2. **Release Notes:** Update README.md bij elke release

3. **Test eerst lokaal:** Test package lokaal voor je publiceert

4. **Tag releases in Git:**
   ```bash
   git tag -a v1.0.0 -m "Release 1.0.0"
   git push origin v1.0.0
   ```

## 🎉 Klaar!

Nu kun je gewoon in Visual Studio:
1. **F6** → Build
2. **Right-click → Pack** → NuGet package maken
3. **Push naar NuGet.org** → Publiceren

Geen scripts nodig! 🚀
