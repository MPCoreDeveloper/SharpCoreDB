# SharpCoreDB v1.4.1 - Production Database Engine

**High-Performance Embedded Database for .NET 10**

SharpCoreDB is a modern, encrypted, file-based database engine with SQL support, built for production applications.

## ✨ What's New in v1.4.1

### 🐛 Critical Bug Fixes
- **Database Reopen:** Fixed edge case where closing and immediately reopening a database would fail
- **Metadata Handling:** Graceful empty JSON handling for new databases
- **Durability:** Immediate metadata flush ensures persistence on disk

### 📦 New Features
- **Brotli Compression:** 60-80% smaller metadata files with zero CPU overhead
- **Backward Compatible:** Auto-detects compressed vs raw JSON format
- **Enterprise Distributed:** Phase 10 complete with sync, replication, transactions

## 🚀 Key Features

✅ **Embedded Database** - Single-file storage, no server required  
✅ **Encrypted** - AES-256-GCM encryption built-in  
✅ **SQL Support** - Full SQL syntax, prepared statements  
✅ **High Performance** - 6.5x faster than SQLite for bulk operations  
✅ **Modern C# 14** - Latest language features, NativeAOT ready  
✅ **Cross-Platform** - Windows, Linux, macOS, ARM64 native  
✅ **Production Ready** - 1,468+ tests, zero known critical bugs  

## 📊 Performance

- **Bulk Insert (1M rows):** 2.8 seconds
- **Analytics (COUNT 1M):** 682x faster than SQLite
- **Vector Search:** 50-100x faster than SQLite
- **Metadata Compression:** <1ms overhead

## 🔗 Package Ecosystem

This package installs the core database engine. Extensions available:

- **SharpCoreDB.Analytics** - 100+ aggregate & window functions (150-680x faster)
- **SharpCoreDB.VectorSearch** - SIMD-accelerated semantic search (50-100x faster)
- **SharpCoreDB.Graph** - Lightweight graph traversal (30-50% faster)
- **SharpCoreDB.Distributed** - Multi-master replication, sharding, transactions
- **SharpCoreDB.Provider.Sync** - Dotmim.Sync integration (bidirectional sync)

## 📚 Documentation

**Full docs:** https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/INDEX.md

**Version 1.4.1 docs:**
- [Metadata Improvements](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/storage/METADATA_IMPROVEMENTS_V1.4.1.md)
- [Progression Report](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/PROGRESSION_V1.3.5_TO_V1.4.1.md)
- [Quick Reference](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/storage/QUICK_REFERENCE_V1.4.1.md)

## 💻 Quick Example

```csharp
using SharpCoreDB;

// Create database
var factory = new DatabaseFactory();
var db = factory.Create("myapp.scdb", "master-password");

// Execute SQL
db.ExecuteSQL("CREATE TABLE users (id INT PRIMARY KEY, name TEXT)");
db.ExecuteSQL("INSERT INTO users VALUES (1, 'Alice')");

// Query data
var results = db.ExecuteQuery("SELECT * FROM users WHERE id = 1");
foreach (var row in results)
{
    Console.WriteLine($"{row["id"]}: {row["name"]}");
}

db.Flush(); // Persist to disk
```

## 🏆 Production Features

- **ACID Compliance** - Full transaction support with WAL
- **Backup & Recovery** - Point-in-time recovery, checkpoint management
- **Concurrency** - Thread-safe operations, connection pooling
- **Multi-Tenant** - Row-level security, schema isolation
- **Enterprise Sync** - Bidirectional sync with PostgreSQL, SQL Server, MySQL
- **Monitoring** - Health checks, metrics, performance stats

## 🔒 Security

- AES-256-GCM encryption for sensitive data
- Password-based key derivation (PBKDF2)
- No plaintext passwords or keys in memory
- Audit logging support

## 📈 Performance Optimizations

- Tiered JIT with PGO (1.2-2x improvement)
- SIMD vectorization where applicable
- Memory-mapped I/O for fast reads
- Batched writes for high throughput
- Query plan caching

## 🛠️ Use Cases

- **Time Tracking Apps** - Embedded, encrypted, offline-first
- **Invoicing Systems** - Multi-tenant, backup-friendly
- **AI/RAG Agents** - Vector search, knowledge base
- **IoT/Edge Devices** - ARM64 native, minimal footprint
- **Mobile Apps** - Sync with cloud database
- **Desktop Applications** - Single-file deployment

## 📦 Installation

```bash
dotnet add package SharpCoreDB
```

## 🔄 Upgrade from v1.3.5

**100% backward compatible** - No breaking changes!

```bash
dotnet add package SharpCoreDB --version 1.4.1
```

Your existing databases work as-is. New metadata is automatically compressed.

## 🐛 Bug Reporting

Found an issue? Report it on GitHub: https://github.com/MPCoreDeveloper/SharpCoreDB/issues

## 📄 License

MIT License - See LICENSE file in the repository

## 🙏 Contributing

We welcome contributions! Check the repository for contribution guidelines.

---

**Latest Version:** 1.4.1 (Feb 20, 2026)  
**Target:** .NET 10 / C# 14  
**Tests:** 1,468+ (100% passing)  
**Status:** ✅ Production Ready

