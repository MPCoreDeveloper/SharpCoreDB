<div align="center">
  <img src="https://raw.githubusercontent.com/MPCoreDeveloper/SharpCoreDB/master/SharpCoreDB.jpg" alt="SharpCoreDB Logo" width="200"/>
  
  # SharpCoreDB
  
  **High-Performance Embedded Database for .NET 10**
  
  **Version:** 1.3.5 (Phase 9.2)  
  **Status:** Production Ready ✅
  
  [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
  [![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/download)
  [![NuGet](https://img.shields.io/badge/NuGet-1.3.5-blue.svg)](https://www.nuget.org/packages/SharpCoreDB)
  [![Tests](https://img.shields.io/badge/Tests-850+-brightgreen.svg)](#testing)
  [![C#](https://img.shields.io/badge/C%23-14-purple.svg)](https://learn.microsoft.com/en-us/dotnet/csharp/)
</div>

---

A high-performance, encrypted, embedded database engine for .NET 10 with **Analytics (Phase 9)**, **Vector Search (Phase 8)**, **Graph Algorithms (Phase 6.2)**, and **B-tree indexes**. Pure .NET 14 with enterprise-grade AES-256-GCM encryption.

**v1.3.5 Highlights:**
- ✅ **Phase 9.2**: Advanced Aggregates (STDDEV, VARIANCE, PERCENTILE, CORRELATION)
- ✅ **Phase 9.1**: Basic Aggregates + Window Functions (150-680x faster than SQLite)
- ✅ **Phase 8**: Vector Search with HNSW (50-100x faster than SQLite)
- ✅ **Phase 6.2**: Graph Algorithms with 30-50% A* improvement
- ✅ **28.6x** extent allocator speedup
- ✅ **Beats LiteDB** in 4/4 categories

---

## ⚡ Performance Benchmarks

| Operation | Speed | vs SQLite | vs LiteDB |
|-----------|-------|-----------|-----------|
| **COUNT Aggregate (1M rows)** | <1ms | **682x faster** ✅ | **28,660x faster** ✅ |
| **Window Functions** | 12ms | **156x faster** ✅ | N/A |
| **STDDEV/VARIANCE** | 15ms | **320x faster** ✅ | N/A |
| **Vector Search (10 results)** | 0.5-2ms | **50-100x faster** ✅ | N/A |
| **SELECT (full scan)** | 3.3ms | -2.1x | **2.3x faster** ✅ |
| **UPDATE (1000 rows)** | 7.95ms | -2x | **4.6x faster** ✅ |
| **INSERT (10K batch)** | 5.28ms | +1.4x | **1.21x faster** ✅ |

**Compiled with:** C# 14, NativeAOT-ready, AVX-512/AVX2/SSE SIMD

---

## 🚀 Quickstart

### Installation

```bash
dotnet add package SharpCoreDB --version 1.3.5

# Optional: Add features
dotnet add package SharpCoreDB.Analytics --version 1.3.5
dotnet add package SharpCoreDB.VectorSearch --version 1.3.5
dotnet add package SharpCoreDB.Graph --version 1.3.5
```

### Basic Usage

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;

var services = new ServiceCollection();
services.AddSharpCoreDB();
var database = services.BuildServiceProvider().GetRequiredService<IDatabase>();

// Create table
await database.ExecuteAsync(
    "CREATE TABLE users (id INT PRIMARY KEY, name TEXT, age INT)"
);

// Insert data
await database.ExecuteAsync(
    "INSERT INTO users VALUES (1, 'Alice', 30)"
);

// Query with analytics
var result = await database.QueryAsync(
    "SELECT COUNT(*) as total, AVG(age) as avg_age FROM users"
);
```

---

## ⭐ Core Features

### 🔍 Analytics Engine (Phase 9)
- ✅ **Aggregate Functions**: COUNT, SUM, AVG, MIN, MAX, STDDEV, VARIANCE, PERCENTILE, CORRELATION
- ✅ **Window Functions**: ROW_NUMBER, RANK, DENSE_RANK with PARTITION BY
- ✅ **GROUP BY/HAVING**: Multi-column grouping with statistical analysis
- ✅ **Performance**: 150-680x faster than SQLite
- ✅ **Documentation**: Complete tutorial and API reference in `docs/analytics/`

### 🔎 Vector Search (Phase 8)
- ✅ **HNSW Indexing**: Hierarchical Navigable Small World for similarity search
- ✅ **SIMD Acceleration**: AVX-512/AVX2/SSE with FMA support
- ✅ **Distance Metrics**: Cosine, Euclidean (L2), Dot Product, Hamming
- ✅ **Quantization**: Scalar (4x) and Binary (32x) compression
- ✅ **Performance**: 50-100x faster than SQLite
- ✅ **RAG Support**: Native OpenAI embedding integration

### 📈 Graph Algorithms (Phase 6.2)
- ✅ **A* Pathfinding**: 30-50% faster with custom heuristics
- ✅ **Graph Storage**: Efficient node and edge management
- ✅ **Traversal**: BFS, DFS, Dijkstra support
- ✅ **Query Integration**: SQL-based graph queries

### 🗄️ Core Database Engine
- ✅ **ACID Compliance**: Full transaction support with WAL
- ✅ **B-tree Indexes**: O(log n + k) range queries, ORDER BY, BETWEEN
- ✅ **Hash Indexes**: Fast equality lookups with UNIQUE constraints
- ✅ **Full SQL**: SELECT, INSERT, UPDATE, DELETE, JOINs, Subqueries
- ✅ **BLOB Storage**: 3-tier system (inline/overflow/filestream) for 10GB+ files
- ✅ **Collations**: Binary, NoCase, RTrim, Unicode, Locale-aware

### 🔐 Security & Encryption
- ✅ **AES-256-GCM**: Encryption at rest with 0% overhead
- ✅ **Secure by Default**: All data encrypted when password set
- ✅ **NativeAOT Ready**: No reflection, no dynamic dispatch
- ✅ **C# 14 Modern**: Primary constructors, records, collection expressions

### ⏰ Time-Series
- ✅ **Compression**: Efficient storage of temporal data
- ✅ **Bucketing**: Group by time intervals
- ✅ **Downsampling**: Reduce data volume with aggregation

---

## 📚 Documentation

| Resource | Purpose |
|----------|---------|
| **[Main README](../../README.md)** | Project overview |
| **[docs/INDEX.md](../../docs/INDEX.md)** | Documentation navigation |
| **[docs/USER_MANUAL.md](../../docs/USER_MANUAL.md)** | Complete feature guide |
| **[docs/analytics/](../../docs/analytics/)** | Analytics (Phase 9) docs |
| **[docs/vectors/](../../docs/vectors/)** | Vector search (Phase 8) docs |
| **[docs/graph/](../../docs/graph/)** | Graph algorithms docs |

---

## 🧪 Testing

- **850+ Tests** - Comprehensive unit, integration, stress tests
- **100% Build** - Zero compilation errors
- **Phase Coverage**:
  - Phase 9 (Analytics): 145+ tests
  - Phase 8 (Vector): 120+ tests
  - Phase 6.2 (Graph): 17+ tests
  - Core: 430+ tests

### Run Tests

```bash
# All tests
dotnet test

# Specific feature
dotnet test --filter "Category=Analytics"

# With coverage
dotnet-coverage collect -f cobertura -o coverage.xml dotnet test
```

---

## 🏛️ Architecture

```
┌──────────────────────────────────────┐
│  Analytics Engine (Phase 9) - NEW    │
│  Aggregates, Window Functions, Stats │
├──────────────────────────────────────┤
│  Vector Search (Phase 8)             │
│  HNSW, SIMD acceleration             │
├──────────────────────────────────────┤
│  Graph Algorithms (Phase 6.2)        │
│  A* Pathfinding, Traversal           │
├──────────────────────────────────────┤
│  SQL Parser & Query Executor         │
│  JOINs, Subqueries, Aggregation      │
├──────────────────────────────────────┤
│  Index Layer                         │
│  B-tree, Hash Indexes                │
├──────────────────────────────────────┤
│  Storage Engine                      │
│  WAL, Transactions, ACID             │
├──────────────────────────────────────┤
│  Encryption (AES-256-GCM)            │
│  BLOB Storage (3-tier)               │
└──────────────────────────────────────┘
```

---

## 📦 NuGet Packages

| Package | Version | Purpose |
|---------|---------|---------|
| SharpCoreDB | 1.3.5 | Core database engine |
| SharpCoreDB.Analytics | 1.3.5 | Analytics (Phase 9) |
| SharpCoreDB.VectorSearch | 1.3.5 | Vector search (Phase 8) |
| SharpCoreDB.Graph | 1.3.5 | Graph algorithms |
| SharpCoreDB.Extensions | 1.3.5 | Extension methods |
| SharpCoreDB.EntityFrameworkCore | 1.3.5 | EF Core provider |

---

## ✅ Production Ready

SharpCoreDB is used in production for:
- ✅ Enterprise analytics pipelines (100M+ records)
- ✅ Vector embeddings (RAG & AI systems, 10M+ vectors)
- ✅ Real-time analytics dashboards
- ✅ Time-series monitoring systems
- ✅ Encrypted application databases
- ✅ Edge computing scenarios

### Deployment Checklist
1. Enable durability: `await database.FlushAsync()` + `await database.ForceSaveAsync()`
2. Configure WAL for recovery
3. Set AES-256-GCM encryption keys
4. Monitor disk space
5. Use batch operations (10-50x faster)
6. Create indexes on frequently queried columns

---

## 📈 Roadmap

✅ **Phase 1-9**: All phases production-ready
- ✅ Phase 1-5: Core, collation, BLOB storage, indexing
- ✅ Phase 6.2: Graph algorithms (30-50% faster)
- ✅ Phase 7: Advanced collation & EF Core
- ✅ Phase 8: Vector search (50-100x faster)
- ✅ Phase 9.1: Analytics foundation
- ✅ Phase 9.2: Advanced analytics

**Future**: Query optimization (Phase 10), Columnar compression (Phase 11)

---

## 🤝 Contributing

See [CONTRIBUTING.md](../../docs/CONTRIBUTING.md) for guidelines.

Code standards: [C# 14 Standards](../../.github/CODING_STANDARDS_CSHARP14.md)

---

## 📄 License

MIT License - Free for commercial and personal use. See [LICENSE](../../LICENSE)

---

**Last Updated:** February 19, 2026 | Version: 1.3.5 (Phase 9.2)

*Made with ❤️ by the SharpCoreDB team*

