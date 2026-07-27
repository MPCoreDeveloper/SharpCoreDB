# SharpCoreDB.Functional.Linq2DB

linq2db adapter for `SharpCoreDB.Functional`.

**Version:** `v1.9.2`  
**Package:** `SharpCoreDB.Functional.Linq2DB`

---

## Overview

**SharpCoreDB.Functional.Linq2DB** bridges linq2db's powerful LINQ query capabilities with SharpCoreDB's functional programming patterns. Get compile-time type safety, expressive LINQ queries, and low-overhead execution—without the heavy change tracking of Entity Framework Core.

**Perfect for:**
- ✅ High-throughput AI agentic workflows
- ✅ Compile-time type safety with LINQ expressions
- ✅ Low memory overhead (no change tracking)
- ✅ Native AOT-compatible query patterns
- ✅ Railway-oriented programming (Option/Fin/Seq)

---

## Installation

```bash
dotnet add package SharpCoreDB.Functional.Linq2DB --version 1.9.2
```

---

## Quick Start

### Basic Usage

```csharp
using SharpCoreDB.Functional.Linq2DB;

// Create connection
using var connection = new SharpCoreDBDataConnection("Path=./mydata.scdb");

// LINQ queries
var users = await connection.GetTable<User>()
    .Where(u => u.IsActive)
    .OrderBy(u => u.Email)
    .ToListAsync();
```

### Functional API

```csharp
var functionalDb = new FunctionalLinq2DbContext(connection);

// Returns Option<User>
var user = await functionalDb.FindOneAsync<User>(u => u.Email == "test@example.com");

// Returns Fin<Unit>
var result = await functionalDb.InsertAsync(new User { Email = "new@example.com" });
```

---

## Documentation

- 📖 Full documentation in repository
- 🔗 [SharpCoreDB GitHub](https://github.com/MPCoreDeveloper/SharpCoreDB)

---

**License:** MIT
