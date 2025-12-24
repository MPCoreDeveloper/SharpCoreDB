# SharpCoreDB.EntityFrameworkCore

EF Core integration for SharpCoreDB.

Install
- dotnet add package SharpCoreDB.EntityFrameworkCore

Setup
```csharp
// services.AddDbContext<YourDbContext>(o => o.UseSharpCoreDB("Data Source=..."));
```

Notes
- Depends on SharpCoreDB and Microsoft.EntityFrameworkCore
