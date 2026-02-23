<div align="center">
  <img src="https://raw.githubusercontent.com/MPCoreDeveloper/SharpCoreDB/master/SharpCoreDB.jpg" alt="SharpCoreDB Logo" width="200"/>
  
  # SharpCoreDB
  
  **High-Performance Embedded Database for .NET 10**
  
  [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
  [![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/download)
  [![NuGet](https://img.shields.io/badge/NuGet-1.3.5-blue.svg)](https://www.nuget.org/packages/SharpCoreDB)
  [![Build](https://img.shields.io/badge/Build-✅_Passing-brightgreen.svg)](https://github.com/MPCoreDeveloper/SharpCoreDB)
  [![Tests](https://img.shields.io/badge/Tests-850+_Passing-brightgreen.svg)](https://github.com/MPCoreDeveloper/SharpCoreDB)
  [![C#](https://img.shields.io/badge/C%23-14-purple.svg)](https://learn.microsoft.com/en-us/dotnet/csharp/)
</div>

---

## 📌 **Current Status — v1.4.0 (February 20, 2026)**

### ✅ **Production-Ready: Phase 10 Distributed Features Complete**

**SharpCoreDB now supports enterprise-scale distributed databases with multi-master replication, conflict resolution, and bidirectional synchronization with SQL Server via Dotmim.Sync.**

#### 🎯 Latest Achievements (v1.3.5 → v1.4.0)

- **Phase 10.1: Dotmim.Sync Integration** ✅
  - Bidirectional sync with SQL Server, PostgreSQL, MySQL, SQLite
  - Multi-tenant filtering for AI agent architectures
  - Enterprise-grade conflict resolution
  
- **Phase 10.2: Multi-Master Replication** ✅
  - Vector clock-based causality tracking
  - Automatic conflict resolution strategies
  - Real-time replication monitoring
  
- **Phase 10.3: Distributed Transactions** ✅
  - Two-phase commit protocol across shards
  - Transaction recovery and failover
  - Cross-shard consistency guarantees
  
- **Phase 9.2: Advanced Aggregate Functions** ✅
  - Complex aggregates: STDDEV, VARIANCE, CORRELATION, PERCENTILE
  - Histogram and bucketing functions
  - Statistical analysis capabilities
  
- **Phase 9.1: Analytics Engine Foundation** ✅
  - Basic aggregates: COUNT, SUM, AVG, MIN, MAX
  - Window functions: ROW_NUMBER, RANK, DENSE_RANK
  - Partition and ordering support
  
- **Phase 8: Vector Search Integration** ✅
  - HNSW indexing with SIMD acceleration
  - 50-100x faster than SQLite
  - Production-tested with 10M+ vectors

- **Phase 6.2: A* Pathfinding Optimization** ✅
  - 30-50% performance improvement
  - Custom heuristics for graph traversal
  - 17 comprehensive tests

- **Enhanced Locale Validation** ✅
  - Strict validation rejects invalid locales
  - EF Core COLLATE support
  - 28.6x ExtentAllocator improvement

#### 📦 Installation

```bash
# Core database
dotnet add package SharpCoreDB --version 1.4.0

# Distributed features (NEW)
dotnet add package SharpCoreDB.Distributed --version 1.4.0

# Dotmim.Sync integration (optional - choose your target database)
dotnet add package Dotmim.Sync.Core --version 1.3.0
dotnet add package Dotmim.Sync.SqlServer --version 1.3.0      # For SQL Server
dotnet add package Dotmim.Sync.PostgreSQL --version 1.3.0   # For PostgreSQL
dotnet add package Dotmim.Sync.MySQL --version 1.3.0        # For MySQL
dotnet add package Dotmim.Sync.SQLite --version 1.3.0       # For SQLite
```

---

## 🚀 **Features Overview**

### ✅ **Production-Ready: Phase 10 Distributed Features Complete**
- Multi-master replication with conflict resolution
- Bidirectional synchronization with SQL Server, PostgreSQL, MySQL, SQLite
- Advanced analytic functions and time-series capabilities
- Custom collation and A* pathfinding optimization

### ⚙️ **Core Database Engine Enhancements**
- Faster query performance with SIMD-accelerated VM
- Efficient storage engine with universal file format
- Comprehensive JSON support: parsing, querying, indexing
- Full-text search with customizable stemming and tokenization

### 🔒 **Security and Compliance**
- TLS 1.2+ encryption for data in transit
- AES-256 encryption for data at rest
- Fine-grained access control and auditing
- GDPR and CCPA compliance features

### 💻 **Cross-Platform Sync** - Bidirectional sync with SQL Server, PostgreSQL, MySQL, SQLite
### ✅ **Dotmim.Sync Integration** - Enterprise-grade synchronization framework

