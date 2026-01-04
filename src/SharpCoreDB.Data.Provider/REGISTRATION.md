# SharpCoreDB ADO.NET Data Provider - SSMS Registration Guide

Complete guide for registering the SharpCoreDB ADO.NET Data Provider in SQL Server Management Studio (SSMS) and other ADO.NET tools.

## Quick Start: Automated Installation

### ?? Recommended: PowerShell Installer (Easiest)
Run as Administrator:
```powershell
cd SharpCoreDB.Data.Provider
.\Install-SharpCoreDBProvider.ps1
```

**With GAC installation (recommended for SSMS):**
```powershell
.\Install-SharpCoreDBProvider.ps1 -InstallToGAC
```

**Verify installation:**
```powershell
.\Test-SharpCoreDBProvider.ps1 -CheckGAC
```

**Uninstall:**
```powershell
.\Uninstall-SharpCoreDBProvider.ps1 -UninstallFromGAC
```

### ?? Alternative: C# Installer
Run as Administrator:
```cmd
SharpCoreDB.Installer.exe
```

**With GAC installation:**
```cmd
SharpCoreDB.Installer.exe /gac
```

**Uninstall:**
```cmd
SharpCoreDB.Installer.exe /uninstall /gac
```

**Silent mode:**
```cmd
SharpCoreDB.Installer.exe /silent /gac
```

---

## Overview
The SharpCoreDB Data Provider can be registered for use in:
- SQL Server Management Studio (SSMS)
- Visual Studio Server Explorer
- Any ADO.NET-compatible tool

## Registration Options

### Option 1: Programmatic Registration (.NET 10+)
**Note:** This registers the provider **only for your running application**. It is NOT available in SSMS or other tools.

For modern .NET applications:
```csharp
using System.Data.Common;
using SharpCoreDB.Data.Provider;

// Register the provider
DbProviderFactories.RegisterFactory("SharpCoreDB.Data.Provider", SharpCoreDBProviderFactory.Instance);

// Use the provider
var factory = DbProviderFactories.GetFactory("SharpCoreDB.Data.Provider");
using var connection = factory.CreateConnection();
connection!.ConnectionString = "Path=C:\\data\\mydb.scdb;Password=secret";
connection.Open();
```

### Option 2: app.config Registration (.NET Framework)
**Note:** This registers the provider **only for your application**. It is NOT available in SSMS or other tools.

Add to your `app.config`:
```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <system.data>
    <DbProviderFactories>
      <add name=".NET Framework Data Provider for SharpCoreDB"
           invariant="SharpCoreDB.Data.Provider"
           description="SharpCoreDB ADO.NET Data Provider"
           type="SharpCoreDB.Data.Provider.SharpCoreDBProviderFactory, SharpCoreDB.Data.Provider, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null" />
    </DbProviderFactories>
  </system.data>
  
  <connectionStrings>
    <add name="SharpCoreDB"
         providerName="SharpCoreDB.Data.Provider"
         connectionString="Path=C:\data\mydb.scdb;Password=MySecretPassword" />
  </connectionStrings>
</configuration>
```

### Option 3: machine.config Registration (System-wide for SSMS)
**?? Important:** This is the **ONLY way** to make the provider available in SSMS and other external tools.

**Requires Administrator rights and affects all applications on the machine.**

**? Recommended:** Use the automated installation scripts above instead of manual registration.

#### Manual Registration (if needed):

1) Locate `machine.config` (depending on .NET version):
- 64-bit: `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\Config\machine.config`
- 32-bit: `C:\Windows\Microsoft.NET\Framework\v4.0.30319\Config\machine.config`

2) Make a backup:
```powershell
Copy-Item "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\Config\machine.config" `
          "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\Config\machine.config.backup"
```

3) Add provider registration under `<system.data>`:
```xml
<system.data>
  <DbProviderFactories>
    <add name=".NET Framework Data Provider for SharpCoreDB"
         invariant="SharpCoreDB.Data.Provider"
         description="SharpCoreDB ADO.NET Data Provider"
         type="SharpCoreDB.Data.Provider.SharpCoreDBProviderFactory, SharpCoreDB.Data.Provider" />
  </DbProviderFactories>
</system.data>
```

4) (Optional but recommended) Register assemblies to GAC:
```powershell
# Using gacutil (Visual Studio Developer Command Prompt)
gacutil /i "C:\path\to\SharpCoreDB.Data.Provider.dll"
gacutil /i "C:\path\to\SharpCoreDB.dll"

# Or copy to GAC folder manually if needed
```

## SSMS Configuration
1. Register the provider (Option 3 above or use automated installer).
2. Restart SSMS.
3. Connect in SSMS: `Connect -> Database Engine -> Options -> Additional Connection Parameters`
```
Provider=SharpCoreDB.Data.Provider;Path=C:\data\mydb.scdb;Password=secret
```
**Note:** Full SSMS browsing may require additional schema discovery APIs in SharpCoreDB.

## Connection String Formats
- Basic: `Path=C:\data\mydb.scdb;Password=MySecretPassword`
- Alias: `Data Source=C:\data\mydb.scdb;Password=MySecretPassword`

| Parameter          | Required | Description                                       |
|--------------------|----------|---------------------------------------------------|
| `Path`/`Data Source` | Yes      | Full path to .scdb database file/directory        |
| `Password`         | Yes      | Master password for the database                  |

## Verification
```csharp
using System.Data.Common;

var factories = DbProviderFactories.GetFactoryClasses();
var row = factories.Rows
    .Cast<System.Data.DataRow>()
    .FirstOrDefault(r => r["InvariantName"].ToString() == "SharpCoreDB.Data.Provider");

if (row != null)
{
    Console.WriteLine("? SharpCoreDB provider is registered");
    Console.WriteLine($"Name: {row["Name"]}");
    Console.WriteLine($"Description: {row["Description"]}");
}
else
{
    Console.WriteLine("? SharpCoreDB provider is NOT registered");
}
```

## Troubleshooting
- **Provider not found**: verify config, restart app/SSMS, check .NET version
- **Assembly load failed**: ensure DLLs are in bin/GAC, or add probing path via `<assemblyBinding>`
- **Core DLL not found**: place `SharpCoreDB.Data.Provider.dll` and `SharpCoreDB.dll` together
- **Installation scripts not working**: ensure you run as Administrator
- **GAC installation failed**: install Windows SDK or Visual Studio (includes gacutil.exe)

## Best Practices
- **Development**: programmatic registration (Option 1)
- **Testing**: app.config registration (Option 2)
- **Production/SSMS**: automated installer with GAC (recommended)
- **System-wide deployment**: machine.config + GAC (Option 3)

## Deployment Checklist
- [ ] Build SharpCoreDB.Data.Provider.dll
- [ ] Ensure SharpCoreDB.dll is present
- [ ] Run installer as Administrator
- [ ] Choose GAC installation for SSMS support
- [ ] Restart SSMS and Visual Studio
- [ ] Test connection string
- [ ] (Optional) Run verification script

## Available Scripts
- **Install-SharpCoreDBProvider.ps1** - Installs provider system-wide
- **Uninstall-SharpCoreDBProvider.ps1** - Removes provider registration
- **Test-SharpCoreDBProvider.ps1** - Verifies installation
- **SharpCoreDB.Installer.exe** - C# console installer (alternative)

## License
MIT License - Copyright (c) 2025-2026 MPCoreDeveloper
