# SharpCoreDB Examples

Deze directory bevat praktijkvoorbeelden voor het gebruik van SharpCoreDB in verschillende scenario's.

## 📁 Directory Structuur

```
examples/
├── sync/                          # Synchronisatie voorbeelden
│   ├── SyncExample.cs            # Basis Dotmim.Sync integratie
│   └── CrossPlatformSyncExample.cs # Cross-platform sync (SQL Server, PostgreSQL, etc.)
└── README.md                     # Deze file
```

## 🔄 Synchronisatie Voorbeelden

### Basis Synchronisatie
**Bestand:** `sync/SyncExample.cs`

Toont hoe SharpCoreDB te synchroniseren met SQL Server via Dotmim.Sync:

```csharp
using Dotmim.Sync;
using Dotmim.Sync.SqlServer;
using SharpCoreDB.Provider.Sync;

// Configureer SharpCoreDB als lokale provider
var sharpcoredbProvider = new SharpCoreDBSyncProvider("Data Source=local.db");

// Configureer SQL Server als remote provider
var sqlServerProvider = new SqlSyncProvider("Server=mssql;Database=sync;Trusted_Connection=True;");

// Maak sync agent
var agent = new SyncAgent(sharpcoredbProvider, sqlServerProvider);

// Definieer tabellen om te syncen
var tables = new string[] { "Users", "Orders", "Products" };

// Voer bidirectionele sync uit
var result = await agent.SynchronizeAsync(tables);

Console.WriteLine($"Geüpdatet: ↑{result.TotalChangesUploaded} ↓{result.TotalChangesDownloaded}");
```

### Cross-Platform Synchronisatie
**Bestand:** `sync/CrossPlatformSyncExample.cs`

Demonstreert synchronisatie met meerdere database types:

```csharp
// Sync met PostgreSQL
var postgresProvider = new PostgreSqlSyncProvider("Server=postgres;Database=sync;User Id=user;Password=pass;");
var pgAgent = new SyncAgent(sharpcoredbProvider, postgresProvider);
await pgAgent.SynchronizeAsync(tables);

// Sync met MySQL
var mysqlProvider = new MySqlSyncProvider("Server=mysql;Database=sync;Uid=user;Pwd=pass;");
var mysqlAgent = new SyncAgent(sharpcoredbProvider, mysqlProvider);
await mysqlAgent.SynchronizeAsync(tables);
```

## 🚀 Hoe Uitvoeren

### 1. Dependencies Installeren

```bash
# Voor basis sync
dotnet add package SharpCoreDB.Provider.Sync
dotnet add package Dotmim.Sync.Core
dotnet add package Dotmim.Sync.SqlServer

# Voor cross-platform sync
dotnet add package Dotmim.Sync.PostgreSql
dotnet add package Dotmim.Sync.MySql
dotnet add package Dotmim.Sync.Sqlite
```

### 2. Voorbeeld Project Aanmaken

```bash
# Maak nieuw console project
dotnet new console -n SharpCoreDBSyncExample
cd SharpCoreDBSyncExample

# Voeg dependencies toe
dotnet add package SharpCoreDB.Provider.Sync
dotnet add package Dotmim.Sync.Core
dotnet add package Dotmim.Sync.SqlServer

# Kopieer voorbeeld code
# (Kopieer code van sync/SyncExample.cs)
```

### 3. Uitvoeren

```bash
dotnet run
```

## 🎯 Use Cases

### 1. Lokale Eerst Architectuur (AI Agenten)
- SharpCoreDB voor lokale opslag
- Sync met cloud database voor backup/deling
- Offline-first capability

### 2. IoT Edge Computing
- SharpCoreDB op edge devices
- Periodieke sync met centrale database
- Offline data buffering

### 3. Mobile Apps
- Lokale SharpCoreDB database
- Sync met backend bij connectiviteit
- Conflict resolution voor offline changes

### 4. Enterprise Data Warehousing
- Operationele data in SharpCoreDB
- Sync met centrale data warehouse
- ETL processen ondersteunen

## 📋 Vereisten

- **.NET 10** of hoger
- **SharpCoreDB** v1.4.0+
- **Dotmim.Sync** v1.3.0+
- Database-specifieke providers (SQL Server, PostgreSQL, etc.)

## 🔧 Configuratie

### Connection Strings

```csharp
// SharpCoreDB (lokaal)
"Data Source=local.db"

// SQL Server
"Server=mssql;Database=sync;Trusted_Connection=True;"

// PostgreSQL
"Server=postgres;Database=sync;User Id=user;Password=pass;"

// MySQL
"Server=mysql;Database=sync;Uid=user;Pwd=pass;"
```

### Sync Opties

```csharp
var options = new SyncOptions
{
    BatchSize = 1000,                    // Verwerk in batches
    UseBulkOperations = true,            // Gebruik bulk operaties
    ConflictResolutionPolicy = ConflictResolutionPolicy.ServerWins,
    UseCompression = true,               // Comprimeer data
    MaxRetries = 3                       // Retry logica
};
```

## 🐛 Troubleshooting

### Veelvoorkomende Problemen

#### Connection Timeouts
```csharp
// Verhoog timeouts
var options = new SyncOptions
{
    CommandTimeout = TimeSpan.FromMinutes(10),
    BulkCopyTimeout = TimeSpan.FromMinutes(15)
};
```

#### Grote Datasets
```csharp
// Gebruik kleinere batches
var options = new SyncOptions
{
    BatchSize = 500,
    UseCompression = true
};
```

#### Conflicten
```csharp
// Configureer conflict resolution
var options = new SyncOptions
{
    ConflictResolutionPolicy = ConflictResolutionPolicy.ClientWins
};
```

## 📚 Meer Informatie

- **[Dotmim.Sync Documentatie](https://dotmim-sync.readthedocs.io/)**
- **[SharpCoreDB Sync Gids](docs/sync/README.md)**
- **[Distributed Features](docs/distributed/README.md)**

## 🤝 Bijdragen

Voel je vrij om meer voorbeelden toe te voegen! Gebruik dezelfde structuur:

1. Maak subdirectory voor use case (`examples/[category]/`)
2. Voeg `[ExampleName].cs` bestand toe
3. Update deze README
4. Test het voorbeeld

**Voorbeelden horen thuis in `examples/`, niet in `src/`!**
