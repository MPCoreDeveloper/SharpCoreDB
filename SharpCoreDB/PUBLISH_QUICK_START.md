# 🚀 SharpCoreDB - Snel Publiceren naar NuGet

## Voor Visual Studio 2026 Gebruikers

### Stap 1: Build het Project
Druk op **F6** of ga naar `Build → Build Solution`

### Stap 2: Maak NuGet Package
1. Ga naar **Solution Explorer**
2. **Right-click** op `SharpCoreDB` project
3. Kies **Pack**

✅ **Klaar!** Het package staat in `bin\Release\SharpCoreDB.1.0.0.nupkg`

### Stap 3: Publiceren naar NuGet.org

**Optie A: Via Package Manager Console**
1. `Tools → NuGet Package Manager → Package Manager Console`
2. Run:
   ```powershell
   cd SharpCoreDB
   dotnet nuget push "bin\Release\SharpCoreDB.1.0.0.nupkg" --source https://api.nuget.org/v3/index.json --api-key YOUR_API_KEY
   ```

**Optie B: Via Terminal**
1. Open **View → Terminal** (of Ctrl+`)
2. Run:
   ```bash
   dotnet nuget push bin\Release\SharpCoreDB.1.0.0.nupkg --source https://api.nuget.org/v3/index.json --api-key YOUR_API_KEY
   ```

## 🔑 API Key Verkrijgen

1. Ga naar https://www.nuget.org/account/apikeys
2. Klik op **Create**
3. Vul in:
   - Key Name: `SharpCoreDB`
   - Select Scopes: `Push`
   - Select Packages: `*` (of kies `SharpCoreDB`)
4. Klik **Create**
5. **Kopieer** de API key (je ziet deze maar 1x!)

## 📦 Wat zit er in het Package?

Het automatisch gegenereerde package bevat:
- ✅ **AnyCPU** versie (fallback)
- ✅ **Windows x64** (met AVX2 optimalisaties)
- ✅ **Windows ARM64** (met NEON optimalisaties)
- ✅ **Linux x64** (met AVX2 optimalisaties)
- ✅ **Linux ARM64** (met NEON optimalisaties)
- ✅ **macOS x64** (Intel Macs)
- ✅ **macOS ARM64** (Apple Silicon M1/M2/M3)
- ✅ **Logo** (SharpCoreDB.jpg)
- ✅ **README.md**
- ✅ **Documentation XML**

## 🎯 Versienummer Wijzigen

Open `SharpCoreDB.csproj` en pas aan:
```xml
<Version>1.0.1</Version>  <!-- Wijzig hier -->
```

Daarna: **Rebuild** (Ctrl+Shift+B) en **Pack** opnieuw.

## 🧪 Testen Voor Publicatie

### 1. Maak lokale NuGet source
```powershell
dotnet nuget add source "D:\source\repos\MPCoreDeveloper\SharpCoreDB\SharpCoreDB\bin\Release" --name LocalSharpCoreDB
```

### 2. Test in nieuw project
```powershell
mkdir TestApp
cd TestApp
dotnet new console
dotnet add package SharpCoreDB --version 1.0.0 --source LocalSharpCoreDB
```

### 3. Verifieer dat het werkt
Voeg toe aan `Program.cs`:
```csharp
using SharpCoreDB.Platform;

Console.WriteLine(PlatformOptimizations.GetPlatformInfo());
Console.WriteLine($"Architecture: {PlatformOptimizations.PlatformArchitecture}");
Console.WriteLine($"Optimizations: {PlatformOptimizations.OptimizationLevel}");
```

Run: `dotnet run`

## 🔄 Update naar Nieuwe Versie

1. Wijzig `<Version>` in `.csproj`
2. Update release notes in README
3. **Rebuild** (Ctrl+Shift+B)
4. **Pack** (right-click → Pack)
5. **Push** naar NuGet.org
6. **Tag** in Git:
   ```bash
   git tag -a v1.0.1 -m "Release 1.0.1"
   git push origin v1.0.1
   ```

## 📱 Mobile Platforms Toevoegen (Optioneel)

Als je Android/iOS wilt ondersteunen:

1. Open `SharpCoreDB.csproj`
2. Zoek naar gecommente secties:
   ```xml
   <!-- Android platforms (uncomment if building mobile) -->
   <!-- iOS platforms (uncomment if building mobile) -->
   ```
3. Uncomment deze secties
4. Voeg toe aan `<RuntimeIdentifiers>`:
   ```xml
   <RuntimeIdentifiers>win-x64;win-arm64;linux-x64;linux-arm64;osx-x64;osx-arm64;android-arm64;ios-arm64</RuntimeIdentifiers>
   ```
5. Rebuild en Pack

## 🎉 First-Time Publish Checklist

- [ ] README.md is up-to-date
- [ ] Version number is correct
- [ ] Logo (SharpCoreDB.jpg) exists in project root
- [ ] License file is present
- [ ] Tested package locally
- [ ] NuGet.org account created
- [ ] API key generated
- [ ] Project builds without errors
- [ ] Pack succeeds
- [ ] Push to NuGet.org

## 💡 Tips

**Tip 1: Automatische builds bij commit**
Push naar GitHub → GitHub Actions bouwt automatisch → Bij tag wordt gepubliceerd

**Tip 2: Package inhoud bekijken**
Download [NuGet Package Explorer](https://github.com/NuGetPackageExplorer/NuGetPackageExplorer) om `.nupkg` files te inspecteren

**Tip 3: Debug vs Release**
Gebruik altijd **Release** configuratie voor NuGet packages:
```
Build → Configuration Manager → Release
```

## 🆘 Hulp Nodig?

- **Documentatie:** Zie `VISUAL_STUDIO_GUIDE.md`
- **Platform Support:** Zie `PLATFORM_SUPPORT.md`
- **Issues:** https://github.com/MPCoreDeveloper/SharpCoreDB/issues

## 📊 Na Publicatie

Je package is nu beschikbaar op:
```
https://www.nuget.org/packages/SharpCoreDB/
```

Gebruikers kunnen het installeren met:
```bash
dotnet add package SharpCoreDB
```

of

```powershell
Install-Package SharpCoreDB
```

**Gefeliciteerd! 🎉** Je hebt je eerste (of volgende) NuGet package gepubliceerd!
