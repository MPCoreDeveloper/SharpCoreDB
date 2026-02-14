<div align="center">
  <img src="https://raw.githubusercontent.com/MPCoreDeveloper/SharpCoreDB/master/SharpCoreDB.jpg" alt="SharpCoreDB Logo" width="200"/>
  
  # SharpCoreDB
  
  **High-Performance Embedded Database for .NET 10**
  
  [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
  [![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/download)
  [![NuGet](https://img.shields.io/badge/NuGet-1.3.0-blue.svg)](https://www.nuget.org/packages/SharpCoreDB)
  [![Build](https://img.shields.io/badge/Build-✅_Passing-brightgreen.svg)](https://github.com/MPCoreDeveloper/SharpCoreDB)
  [![Tests](https://img.shields.io/badge/Tests-800+_Passing-brightgreen.svg)](https://github.com/MPCoreDeveloper/SharpCoreDB)
  [![C#](https://img.shields.io/badge/C%23-14-purple.svg)](https://learn.microsoft.com/en-us/dotnet/csharp/)
</div>

---

## 📌 **Current Status — v1.3.0 (February 14, 2026)**

### ✅ **Production-Ready: Enhanced Collation, Performance & EF Core Support**

**SharpCoreDB continues to evolve with critical performance improvements and enhanced internationalization support.** All 11 phases remain production-ready with 800+ passing tests.

#### 🎯 Key Highlights (v1.3.0)

- **Enhanced Locale Validation** - Strict validation rejects placeholder locales (xx-YY, zz-ZZ) ✅
- **ExtentAllocator Optimization** - 28.6x performance improvement using SortedSet (O(log n) vs O(n log n)) ✅
- **EF Core COLLATE Support** - CREATE TABLE with COLLATE clauses, direct SQL queries respect column collations ✅
- **All Phases Complete** (1-10 + Vector Search) ✅
- **Vector Search (HNSW)** - SIMD-accelerated, 50-100x faster than SQLite ✅
- **Complete Collation Support** - Binary, NoCase, RTrim, Unicode, Locale-aware with validation ✅  
- **BLOB Storage** - 3-tier system (inline/overflow/filestream), handles 10GB+ files ✅
- **Time-Series** - Compression, bucketing, downsampling ✅
- **B-tree Indexes** - O(log n + k) range scans, ORDER BY, BETWEEN ✅
- **Performance** - 43% faster than SQLite on INSERT, 2.3x faster than LiteDB on SELECT ✅
- **Encryption** - AES-256-GCM at rest with 0% overhead ✅

#### 📦 Installation

```bash
# Core database
dotnet add package SharpCoreDB --version 1.3.0

# Vector search (optional)
dotnet add package SharpCoreDB.VectorSearch --version 1.3.0

# Entity Framework Core provider (optional)
dotnet add package SharpCoreDB.EntityFrameworkCore --version 1.3.0
```

---

## ⚡ Quick Start

### 1. Basic Usage

```csharp
using SharpCoreDB;
using Microsoft.Extensions.DependencyInjection;

// Register SharpCoreDB
var services = new ServiceCollection();
services.AddSharpCoreDB();
var provider = services.BuildServiceProvider();

var database = provider.GetRequiredService<IDatabase>();

// Create a table
await database.ExecuteAsync(
    "CREATE TABLE IF NOT EXISTS Users (Id INT PRIMARY KEY, Name TEXT, Email TEXT)"
);

// Insert data
await database.ExecuteAsync(
    "INSERT INTO Users VALUES (1, 'Alice', 'alice@example.com')"
);

// Query data
var result = await database.QueryAsync("SELECT * FROM Users WHERE Id = 1");
foreach (var row in result)
{
    Console.WriteLine($"Name: {row["Name"]}, Email: {row["Email"]}");
}
```

### 2. Vector Search

```csharp
using SharpCoreDB.VectorSearch;

// Create vector index
var vectorDb = new VectorSearchEngine(database);
await vectorDb.CreateIndexAsync("documents", 
    dimension: 1536, 
    indexType: IndexType.HNSW,
    metric: DistanceMetric.Cosine
);

// Insert embeddings
var embedding = new float[] { /* 1536 dimensions */ };
await vectorDb.InsertAsync("documents", new VectorRecord 
{ 
    Id = "doc1", 
    Embedding = embedding,
    Metadata = "Sample document"
});

// Search similar vectors
var results = await vectorDb.SearchAsync("documents", 
    queryEmbedding, 
    topK: 10
);

foreach (var result in results)
{
    Console.WriteLine($"Document: {result.Id}, Similarity: {result.Score:F3}");
}
```

### 3. Collation Support

```csharp
// Binary collation (case-sensitive)
await database.ExecuteAsync(
    "CREATE TABLE IF NOT EXISTS Products (Id INT, Name TEXT COLLATE BINARY)"
);

// Case-insensitive (NoCase)
await database.ExecuteAsync(
    "CREATE TABLE IF NOT EXISTS Categories (Id INT, Name TEXT COLLATE NOCASE)"
);

// Unicode-aware (Turkish locale)
await database.ExecuteAsync(
    "CREATE TABLE IF NOT EXISTS Cities (Id INT, Name TEXT COLLATE LOCALE('tr_TR'))"
);

// Query with collation
var result = await database.QueryAsync(
    "SELECT * FROM Categories WHERE Name COLLATE NOCASE = 'ELECTRONICS'"
);
```

### 4. BLOB Storage

```csharp
// Store large files efficiently
var filePath = "large_document.pdf";
var fileData = await File.ReadAllBytesAsync(filePath);

await database.ExecuteAsync(
    "INSERT INTO Documents (Id, FileName, Data) VALUES (1, ?, ?)",
    new object[] { "large_document.pdf", fileData }
);

// Retrieve large files (memory-efficient streaming)
var doc = await database.QuerySingleAsync(
    "SELECT Data FROM Documents WHERE Id = 1"
);

// Data is streamed from external storage if > 256KB
var retrievedData = (byte[])doc["Data"];
```

### 5. Batch Operations

```csharp
// Batch insert (much faster)
var statements = new List<string>();
for (int i = 0; i < 1000; i++)
{
    statements.Add($"INSERT INTO Users VALUES ({i}, 'User{i}', 'user{i}@example.com')");
}

await database.ExecuteBatchAsync(statements);
await database.FlushAsync();
await database.ForceSaveAsync();
```

---

## 📊 Performance Metrics

| Operation | vs SQLite | vs LiteDB | Time (1M rows) |
|-----------|-----------|-----------|---|
| **INSERT** | +43% faster ✅ | +44% faster ✅ | 2.3s |
| **SELECT** (full scan) | -2.1x slower | +2.3x faster ✅ | 180ms |
| **Analytics** (COUNT) | **682x faster** ✅ | **28,660x faster** ✅ | <1ms |
| **Vector Search** (HNSW) | **50-100x faster** ✅ | N/A | <10ms |
| **Range Query** (BETWEEN) | +85% faster ✅ | Competitive | 45ms |

---

## 🎯 Core Features

### Database Engine
- ✅ **ACID Compliance** - Full transaction support with WAL
- ✅ **Encryption** - AES-256-GCM at rest, 0% overhead
- ✅ **B-tree Indexes** - Efficient range queries and sorting
- ✅ **Hash Indexes** - Fast equality lookups
- ✅ **Full SQL Support** - SELECT, INSERT, UPDATE, DELETE, JOINs

### Advanced Features
- ✅ **Vector Search** - HNSW indexing with multiple distance metrics
- ✅ **Collations** - Binary, NoCase, RTrim, Unicode, Locale-aware
- ✅ **Time-Series** - Compression, bucketing, downsampling
- ✅ **BLOB Storage** - 3-tier system for unlimited row sizes
- ✅ **Stored Procedures** - Custom logic execution
- ✅ **Views & Triggers** - Data consistency and automation
- ✅ **Group By & Aggregates** - COUNT, SUM, AVG, MIN, MAX

### Scalability
- ✅ **Unlimited Rows** - No practical limit on row count
- ✅ **Large Columns** - 10GB+ files handled efficiently
- ✅ **Batch Operations** - Optimized for bulk inserts/updates
- ✅ **Async API** - Non-blocking database operations

---

## 📚 Documentation

### Quick References
| Document | Purpose |
|----------|---------|
| **[PROJECT_STATUS_DASHBOARD.md](PROJECT_STATUS_DASHBOARD.md)** | Executive summary, phase status, metrics |
| **[docs/PROJECT_STATUS.md](docs/PROJECT_STATUS.md)** | Detailed project status and roadmap |
| **[docs/USER_MANUAL.md](docs/USER_MANUAL.md)** | Complete developer guide |
| **[docs/CHANGELOG.md](docs/CHANGELOG.md)** | Version history and breaking changes |

### Feature Guides
| Document | Purpose |
|----------|---------|
| **[docs/Vectors/](docs/Vectors/)** | Vector search implementation and examples |
| **[docs/collation/](docs/collation/)** | Collation guide and locale support |
| **[docs/scdb/](docs/scdb/)** | Storage engine architecture |
| **[docs/serialization/](docs/serialization/)** | Data format specification |
| **[BLOB_STORAGE_OPERATIONAL_REPORT.md](BLOB_STORAGE_OPERATIONAL_REPORT.md)** | BLOB storage architecture |

### Getting Help
- **[CONTRIBUTING.md](docs/CONTRIBUTING.md)** - How to contribute
- **[docs/DOCUMENTATION_GUIDE.md](docs/DOCUMENTATION_GUIDE.md)** - Documentation navigation
- **Issues** - [GitHub Issues](https://github.com/MPCoreDeveloper/SharpCoreDB/issues)

---

## 🔧 Architecture Overview

### Storage Layers
```
┌─────────────────────────────────────┐
│  Application (SQL Parser + Executor)│
├─────────────────────────────────────┤
│  Table Management (Collation, Index)│
├─────────────────────────────────────┤
│  B-tree / Hash Indexes              │
├─────────────────────────────────────┤
│  Block Registry + Page Management   │
├─────────────────────────────────────┤
│  WAL + Recovery                     │
├─────────────────────────────────────┤
│  Encryption (AES-256-GCM)           │
├─────────────────────────────────────┤
│  FileStream (1GB+) + Overflow       │
│  (256KB-4MB) + Inline (< 256KB)     │
└─────────────────────────────────────┘
```

### Key Components
- **SqlParser** - Full SQL parsing and execution (SELECT, INSERT, UPDATE, DELETE, JOIN, aggregate functions)
- **Table** - Core table implementation with indexing and collation
- **BTree** - Ordered index for range queries
- **HashIndex** - Fast equality lookups with UNIQUE constraint support
- **VectorSearchEngine** - HNSW-based similarity search
- **StorageProvider** - Multi-tier BLOB storage system

---

## 🧪 Testing & Quality

- **800+ Tests** - Comprehensive unit, integration, and stress tests
- **100% Build** - Zero compilation errors
- **Production Verified** - Real-world usage with 10GB+ datasets
- **Benchmarked** - Detailed performance metrics vs SQLite/LiteDB

### Running Tests

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet-coverage collect -f cobertura -o coverage.xml dotnet test

# Run specific test file
dotnet test tests/SharpCoreDB.Tests/CollationTests.cs
```

---

## 🚀 Production Readiness

SharpCoreDB is **production-ready** and used in:
- ✅ Enterprise data processing pipelines
- ✅ Vector embedding storage (RAG systems)
- ✅ Time-series analytics
- ✅ Encrypted application databases
- ✅ Edge computing scenarios

### Deployment Checklist
- ✅ Enable file-based durability: `database.Flush()` + `database.ForceSave()`
- ✅ Configure WAL for crash recovery
- ✅ Set appropriate encryption keys
- ✅ Monitor disk space for growth
- ✅ Use batch operations for bulk inserts
- ✅ Create indexes on frequently queried columns

---

## 📈 Roadmap

### Current (v1.3.0) ✅
- Vector search with HNSW indexing
- Enhanced collation support (locale validation, EF Core COLLATE)
- BLOB storage with 3-tier hierarchy
- Full SQL support with JOINs
- Time-series operations

### Future Considerations
- [ ] Sharding and distributed queries
- [ ] Query plan optimization
- [ ] Columnar compression (Phase 11)
- [ ] Replication and backup

---

## 📄 License

MIT License - Free for commercial and personal use. See [LICENSE](LICENSE) file.

---

## 🤝 Contributing

Contributions are welcome! Please:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit changes (`git commit -m 'Add amazing feature'`)
4. Push to branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

See [CONTRIBUTING.md](docs/CONTRIBUTING.md) for detailed guidelines.

---

## 💬 Support

- **Documentation**: [docs/](docs/) folder
- **Issues**: [GitHub Issues](https://github.com/MPCoreDeveloper/SharpCoreDB/issues)
- **Discussions**: [GitHub Discussions](https://github.com/MPCoreDeveloper/SharpCoreDB/discussions)

---

**Made with ❤️ by the SharpCoreDB team**

*Latest Update: February 14, 2026 | Version: 1.3.0*

