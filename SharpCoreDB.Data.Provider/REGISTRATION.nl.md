# SharpCoreDB ADO.NET Data Provider - SSMS Registratie

Complete handleiding voor het registreren van de SharpCoreDB ADO.NET Data Provider in SQL Server Management Studio (SSMS).

## Overzicht

De SharpCoreDB Data Provider kan worden geregistreerd voor gebruik in:
- SQL Server Management Studio (SSMS)
- Visual Studio Server Explorer
- Andere ADO.NET-compatible tools

## Optie 1: Programmatische Registratie (.NET 10+)

Voor moderne .NET 10 applicaties:

```csharp
using System.Data.Common;
using SharpCoreDB.Data.Provider;

// Registreer de provider
DbProviderFactories.RegisterFactory("SharpCoreDB.Data.Provider", 
    SharpCoreDBProviderFactory.Instance);

// Gebruik de provider
var factory = DbProviderFactories.GetFactory("SharpCoreDB.Data.Provider");
using var connection = factory.CreateConnection();
connection!.ConnectionString = "Path=C:\\data\\mydb.scdb;Password=secret";
connection.Open();
```

## Optie 2: app.config Registratie (.NET Framework)

Voor .NET Framework applicaties, voeg toe aan `app.config`:

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

## Optie 3: machine.config Registratie (Systeem-breed)

**Let op**: Dit vereist Administrator rechten en beïnvloedt alle applicaties op de machine.

### Stap 1: Locatie van machine.config

Locaties (afhankelijk van .NET versie):
- **.NET Framework 4.x (64-bit)**: `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\Config\machine.config`
- **.NET Framework 4.x (32-bit)**: `C:\Windows\Microsoft.NET\Framework\v4.0.30319\Config\machine.config`

### Stap 2: Backup maken

```powershell
# PowerShell als Administrator
Copy-Item "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\Config\machine.config" `
          "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\Config\machine.config.backup"
```

### Stap 3: Provider registratie toevoegen

Open `machine.config` in een text editor (als Administrator) en voeg toe binnen `<system.data>` sectie:

```xml
<system.data>
  <DbProviderFactories>
    <!-- Bestaande providers... -->
    
    <add name=".NET Framework Data Provider for SharpCoreDB"
         invariant="SharpCoreDB.Data.Provider"
         description="SharpCoreDB ADO.NET Data Provider"
         type="SharpCoreDB.Data.Provider.SharpCoreDBProviderFactory, SharpCoreDB.Data.Provider" />
  </DbProviderFactories>
</system.data>
```

### Stap 4: Assemblies naar GAC (Optioneel maar aanbevolen)

Voor systeem-brede toegang, registreer in Global Assembly Cache:

```powershell
# PowerShell als Administrator
# Installeer .NET SDK eerst als je gacutil niet hebt

# Optie 1: Met gacutil (Visual Studio Developer Command Prompt)
gacutil /i "C:\path\to\SharpCoreDB.Data.Provider.dll"
gacutil /i "C:\path\to\SharpCoreDB.dll"

# Optie 2: Zonder gacutil (kopieer naar GAC directory)
Copy-Item "C:\path\to\SharpCoreDB.Data.Provider.dll" `
          "C:\Windows\Microsoft.NET\assembly\GAC_MSIL\SharpCoreDB.Data.Provider\v4.0_1.0.0.0__null\"
```

## SSMS Configuratie

### Stap 1: Provider registreren

Volg Optie 3 (machine.config) hierboven.

### Stap 2: SSMS herstarten

Sluit SSMS volledig en start opnieuw op.

### Stap 3: Verbinding maken

1. Open SSMS
2. Klik op "Connect" ? "Database Engine"
3. Bij "Server type" selecteer je normaal "Database Engine"
4. Bij "Server name" vul je een dummy naam in (bijv. "localhost")
5. Klik op "Options" tab
6. Onder "Additional Connection Parameters" vul je in:

```
Provider=SharpCoreDB.Data.Provider;Path=C:\data\mydb.scdb;Password=secret
```

**Let op**: Volledige SSMS integratie vereist mogelijk aanvullende implementatie van schema discovery APIs in SharpCoreDB zelf.

## Connection String Formaten

### Basis formaat

```
Path=C:\data\mydb.scdb;Password=MySecretPassword
```

### Met Data Source alias

```
Data Source=C:\data\mydb.scdb;Password=MySecretPassword
```

### Parameters

| Parameter | Verplicht | Beschrijving |
|-----------|-----------|--------------|
| `Path` of `Data Source` | Ja | Volledig pad naar .scdb database bestand/directory |
| `Password` | Ja | Master password voor de database |

## Verificatie

Test de registratie met deze code:

```csharp
using System.Data.Common;

// Controleer of provider is geregistreerd
var factories = DbProviderFactories.GetFactoryClasses();
var sharpCoreDbRow = factories.Rows
    .Cast<System.Data.DataRow>()
    .FirstOrDefault(row => row["InvariantName"].ToString() == "SharpCoreDB.Data.Provider");

if (sharpCoreDbRow != null)
{
    Console.WriteLine("? SharpCoreDB provider is geregistreerd!");
    Console.WriteLine($"  Name: {sharpCoreDbRow["Name"]}");
    Console.WriteLine($"  Description: {sharpCoreDbRow["Description"]}");
}
else
{
    Console.WriteLine("? SharpCoreDB provider is NIET geregistreerd");
}
```

## Troubleshooting

### Provider niet gevonden

**Probleem**: `DbProviderFactories.GetFactory()` gooit exception

**Oplossingen**:
1. Controleer of `machine.config` of `app.config` correct is
2. Controleer of assembly path correct is
3. Herstart applicatie/SSMS
4. Controleer .NET versie compatibility

### Assembly laden mislukt

**Probleem**: "Could not load file or assembly 'SharpCoreDB.Data.Provider'"

**Oplossingen**:
1. Plaats DLL in application bin directory
2. Registreer in GAC (zie Stap 4 hierboven)
3. Voeg `<assemblyBinding>` redirect toe aan config

```xml
<runtime>
  <assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">
    <probing privatePath="bin;lib" />
  </assemblyBinding>
</runtime>
```

### SharpCoreDB.dll niet gevonden

**Probleem**: Provider laadt maar SharpCoreDB core DLL niet

**Oplossing**:
- Kopieer beide DLLs naar dezelfde directory:
  - `SharpCoreDB.Data.Provider.dll`
  - `SharpCoreDB.dll`
  - Alle dependencies

## Best Practices

1. **Development**: Gebruik programmatische registratie (Optie 1)
2. **Testing**: Gebruik app.config (Optie 2)
3. **Production**: Overweeg machine.config + GAC (Optie 3)
4. **SSMS**: Verplicht machine.config (Optie 3)

## Deployment Checklist

- [ ] SharpCoreDB.Data.Provider.dll gebouwd
- [ ] SharpCoreDB.dll beschikbaar
- [ ] Provider geregistreerd (programmatisch/config/machine.config)
- [ ] Connection string getest
- [ ] Assemblies in GAC (optioneel, voor SSMS)
- [ ] machine.config backup gemaakt (als van toepassing)
- [ ] SSMS herstart (als van toepassing)

## Meer Informatie

- [ADO.NET DbProviderFactory Documentation](https://docs.microsoft.com/en-us/dotnet/api/system.data.common.dbproviderfactory)
- [SharpCoreDB GitHub](https://github.com/MPCoreDeveloper/SharpCoreDB)
- [NuGet Package](https://www.nuget.org/packages/SharpCoreDB.Data.Provider)

## Licentie

MIT License - Copyright (c) 2025-2026 MPCoreDeveloper
