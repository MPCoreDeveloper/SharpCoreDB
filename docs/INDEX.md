# SharpCoreDB Documentation Index

**Version:** 1.3.5 (Phase 9.2 Complete)  
**Status:** Production Ready ✅

Welcome to SharpCoreDB documentation! This page helps you find the right documentation for your use case.

---

## 🚀 Getting Started

Start here if you're new to SharpCoreDB:

1. **[README.md](../README.md)** - Project overview and quick start
2. **[Installation Guide](#installation)** - Setup instructions
3. **[Quick Start Examples](#quick-start)** - Common use cases

---

## 📚 Documentation by Feature

### Core Database Engine
| Document | Topics |
|----------|--------|
| [User Manual](USER_MANUAL.md) | Complete feature guide, all APIs |
| [src/SharpCoreDB/README.md](../src/SharpCoreDB/README.md) | Core engine documentation |
| [Storage Architecture](storage/README.md) | ACID, transactions, WAL |
| [Serialization Format](serialization/README.md) | Data format specification |

### 📊 Analytics Engine (NEW - Phase 9)
| Document | Topics |
|----------|--------|
| [Analytics Overview](analytics/README.md) | Phase 9 features, aggregates, window functions |
| [Analytics Tutorial](analytics/TUTORIAL.md) | Complete tutorial with examples |
| [src/SharpCoreDB.Analytics/README.md](../src/SharpCoreDB.Analytics/README.md) | Package documentation |
| **New in Phase 9.2:** | STDDEV, VARIANCE, PERCENTILE, CORRELATION |
| **New in Phase 9.1:** | COUNT, SUM, AVG, ROW_NUMBER, RANK |

### 🔍 Vector Search (Phase 8)
| Document | Topics |
|----------|--------|
| [Vector Search Overview](vectors/README.md) | HNSW indexing, semantic search |
| [Vector Search Guide](vectors/IMPLEMENTATION.md) | Implementation details |
| [src/SharpCoreDB.VectorSearch/README.md](../src/SharpCoreDB.VectorSearch/README.md) | Package documentation |
| **Features:** | SIMD acceleration, 50-100x faster than SQLite |

### 📈 Graph Algorithms (Phase 6.2)
| Document | Topics |
|----------|--------|
| [Graph Algorithms Overview](graph/README.md) | A* pathfinding, 30-50% improvement |
| [src/SharpCoreDB.Graph/README.md](../src/SharpCoreDB.Graph/README.md) | Package documentation |

### 🌍 Collation & Internationalization
| Document | Topics |
|----------|--------|
| [Collation Guide](collation/README.md) | Language-aware string comparison |
| [Locale Support](collation/LOCALE_SUPPORT.md) | Supported locales and configuration |

### 💾 BLOB Storage
| Document | Topics |
|----------|--------|
| [BLOB Storage Guide](storage/BLOB_STORAGE.md) | 3-tier storage (inline/overflow/filestream) |
| [BLOB_STORAGE_OPERATIONAL_REPORT.md](BLOB_STORAGE_OPERATIONAL_REPORT.md) | Detailed architecture |

### ⏰ Time-Series
| Document | Topics |
|----------|--------|
| [Time-Series Guide](features/TIMESERIES.md) | Compression, bucketing, downsampling |

### 🔐 Security & Encryption
| Document | Topics |
|----------|--------|
| [Encryption Configuration](architecture/ENCRYPTION.md) | AES-256-GCM setup |
| [Security Best Practices](architecture/SECURITY.md) | Deployment guidelines |

### 🏗️ Architecture
| Document | Topics |
|----------|--------|
| [Architecture Overview](architecture/README.md) | System design, components |
| [Query Plan Cache](QUERY_PLAN_CACHE.md) | Optimization details |
| [Index Implementation](architecture/INDEXING.md) | B-tree and hash indexes |

---

## 🔧 By Use Case

### Building a RAG System
1. Start: [Vector Search Overview](vectors/README.md)
2. Setup: [Vector Search Guide](vectors/IMPLEMENTATION.md)
3. Integrate: [Vector package docs](../src/SharpCoreDB.VectorSearch/README.md)
4. Optimize: [Performance Guide](PERFORMANCE.md)

### Real-Time Analytics Dashboard
1. Setup: [Analytics Overview](analytics/README.md)
2. Tutorial: [Analytics Complete Guide](analytics/TUTORIAL.md)
3. Advanced: [Statistical Analysis](analytics/ADVANCED_STATISTICS.md)
4. Examples: [Analytics package docs](../src/SharpCoreDB.Analytics/README.md)

### High-Volume Data Processing
1. Foundation: [Storage Architecture](storage/README.md)
2. BLOB Storage: [BLOB_STORAGE_OPERATIONAL_REPORT.md](BLOB_STORAGE_OPERATIONAL_REPORT.md)
3. Batch Operations: [User Manual](USER_MANUAL.md#batch-operations)
4. Performance: [PERFORMANCE.md](PERFORMANCE.md)

### Multi-Language Application
1. Collation: [Collation Guide](collation/README.md)
2. Locales: [Locale Support](collation/LOCALE_SUPPORT.md)
3. Setup: [User Manual - Collation Section](USER_MANUAL.md#collation)

### Graph-Based Applications
1. Overview: [Graph Algorithms](graph/README.md)
2. Implementation: [Graph package docs](../src/SharpCoreDB.Graph/README.md)
3. Examples: [Graph tutorial](graph/TUTORIAL.md)

---

## 📋 Installation & Setup

### Quick Install
```bash
# Core database
dotnet add package SharpCoreDB --version 1.3.5

# Add features as needed
dotnet add package SharpCoreDB.Analytics --version 1.3.5
dotnet add package SharpCoreDB.VectorSearch --version 1.3.5
dotnet add package SharpCoreDB.Graph --version 1.3.5
```

### Full Setup Guide
See **[USER_MANUAL.md](USER_MANUAL.md#installation)** for detailed installation instructions.

---

## 🚀 Quick Start

### Example 1: Basic Database
```csharp
using SharpCoreDB;

var services = new ServiceCollection();
services.AddSharpCoreDB();
var database = services.BuildServiceProvider().GetRequiredService<IDatabase>();

// Create table
await database.ExecuteAsync(
    "CREATE TABLE users (id INT PRIMARY KEY, name TEXT)"
);

// Insert data
await database.ExecuteAsync(
    "INSERT INTO users VALUES (1, 'Alice')"
);

// Query
var users = await database.QueryAsync("SELECT * FROM users");
```

### Example 2: Analytics with Aggregates
```csharp
using SharpCoreDB.Analytics;

// Statistical analysis
var stats = await database.QueryAsync(@"
    SELECT 
        COUNT(*) as total,
        AVG(salary) as avg_salary,
        STDDEV(salary) as salary_stddev,
        PERCENTILE(salary, 0.75) as top_25_percent
    FROM employees
");
```

### Example 3: Vector Search
```csharp
using SharpCoreDB.VectorSearch;

// Semantic search
var results = await database.QueryAsync(@"
    SELECT title, vec_distance_cosine(embedding, ?) AS distance
    FROM documents
    ORDER BY distance ASC
    LIMIT 10
", [queryEmbedding]);
```

### Example 4: Graph Algorithms
```csharp
using SharpCoreDB.Graph;

// A* pathfinding
var path = await graphEngine.FindPathAsync(
    start: "NodeA",
    end: "NodeZ",
    algorithm: PathfindingAlgorithm.AStar
);
```

---

## 📖 Project-Specific Documentation

### Packages
| Package | README |
|---------|--------|
| SharpCoreDB (Core) | [src/SharpCoreDB/README.md](../src/SharpCoreDB/README.md) |
| SharpCoreDB.Analytics | [src/SharpCoreDB.Analytics/README.md](../src/SharpCoreDB.Analytics/README.md) |
| SharpCoreDB.VectorSearch | [src/SharpCoreDB.VectorSearch/README.md](../src/SharpCoreDB.VectorSearch/README.md) |
| SharpCoreDB.Graph | [src/SharpCoreDB.Graph/README.md](../src/SharpCoreDB.Graph/README.md) |
| SharpCoreDB.Extensions | [src/SharpCoreDB.Extensions/README.md](../src/SharpCoreDB.Extensions/README.md) |
| SharpCoreDB.EntityFrameworkCore | [src/SharpCoreDB.EntityFrameworkCore/README.md](../src/SharpCoreDB.EntityFrameworkCore/README.md) |

---

## 📊 Changelog & Release Notes

| Version | Document | Notes |
|---------|----------|-------|
| 1.3.5 | [CHANGELOG.md](CHANGELOG.md) | Phase 9.2 analytics complete |
| 1.3.0 | [RELEASE_NOTES_v1.3.0.md](RELEASE_NOTES_v1.3.0.md) | Base version |
| Phase 8 | [RELEASE_NOTES_v6.4.0_PHASE8.md](RELEASE_NOTES_v6.4.0_PHASE8.md) | Vector search |
| Phase 9 | [RELEASE_NOTES_v6.5.0_PHASE9.md](RELEASE_NOTES_v6.5.0_PHASE9.md) | Analytics |

---

## 🎯 Development & Contributing

| Document | Purpose |
|----------|---------|
| [CONTRIBUTING.md](CONTRIBUTING.md) | Contribution guidelines |
| [CODING_STANDARDS_CSHARP14.md](../.github/CODING_STANDARDS_CSHARP14.md) | Code style requirements |
| [PROJECT_STATUS.md](PROJECT_STATUS.md) | Current phase status |

---

## 🔍 Search Documentation

### By Topic
- **SQL Operations**: [USER_MANUAL.md](USER_MANUAL.md)
- **Performance**: [PERFORMANCE.md](PERFORMANCE.md)
- **Architecture**: [architecture/README.md](architecture/README.md)
- **Benchmarks**: [BENCHMARK_RESULTS.md](BENCHMARK_RESULTS.md)

### By Problem
- **Slow queries?** → [PERFORMANCE.md](PERFORMANCE.md)
- **Vector search setup?** → [vectors/README.md](vectors/README.md)
- **Analytics queries?** → [analytics/TUTORIAL.md](analytics/TUTORIAL.md)
- **Multi-language?** → [collation/README.md](collation/README.md)
- **Build large files?** → [storage/BLOB_STORAGE.md](storage/BLOB_STORAGE.md)

---

## 📞 Support & Resources

### Documentation
- Main Documentation: [docs/](.) folder
- API Documentation: Within each package README

### Getting Help
- **Issues**: [GitHub Issues](https://github.com/MPCoreDeveloper/SharpCoreDB/issues)
- **Discussions**: [GitHub Discussions](https://github.com/MPCoreDeveloper/SharpCoreDB/discussions)
- **Contributing**: [CONTRIBUTING.md](CONTRIBUTING.md)

---

## 🗂️ Directory Structure

```
docs/
├── INDEX.md                            # Navigation (you are here)
├── USER_MANUAL.md                      # Complete feature guide
├── CHANGELOG.md                        # Version history
├── PERFORMANCE.md                      # Performance tuning
│
├── analytics/                          # Phase 9 Analytics Engine
│   ├── README.md                       # Overview & quick start
│   └── TUTORIAL.md                     # Complete tutorial
│
├── vectors/                            # Phase 8 Vector Search
│   ├── README.md                       # Overview
│   └── IMPLEMENTATION.md               # Implementation guide
│
├── graph/                              # Phase 6.2 Graph Algorithms
│   ├── README.md                       # Overview
│   └── TUTORIAL.md                     # Examples
│
├── collation/                          # Internationalization
│   ├── README.md                       # Collation guide
│   └── LOCALE_SUPPORT.md               # Locale list
│
├── storage/                            # Storage architecture
│   ├── README.md                       # Storage overview
│   ├── BLOB_STORAGE.md                 # BLOB storage details
│   └── SERIALIZATION.md                # Data format
│
├── architecture/                       # System design
│   ├── README.md                       # Architecture overview
│   ├── ENCRYPTION.md                   # Security
│   ├── INDEXING.md                     # Index details
│   └── SECURITY.md                     # Best practices
│
└── features/                           # Feature guides
    └── TIMESERIES.md                   # Time-series operations
```

---

## ✅ Checklist: Getting Started

- [ ] Read [README.md](../README.md) for overview
- [ ] Install packages via NuGet
- [ ] Run [Quick Start Examples](#quick-start)
- [ ] Read [USER_MANUAL.md](USER_MANUAL.md) for your feature
- [ ] Check [PERFORMANCE.md](PERFORMANCE.md) for optimization
- [ ] Review [CONTRIBUTING.md](CONTRIBUTING.md) if contributing

---

**Last Updated:** February 19, 2026 | Version: 1.3.5 (Phase 9.2)

For questions or issues, please open an issue on [GitHub](https://github.com/MPCoreDeveloper/SharpCoreDB/issues).
