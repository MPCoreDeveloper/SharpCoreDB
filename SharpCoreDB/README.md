<div align="center">
  <img src="https://raw.githubusercontent.com/MPCoreDeveloper/SharpCoreDB/master/SharpCoreDB.jpg" alt="SharpCoreDB Logo" width="200"/>
  
  # SharpCoreDB
  
  **High-Performance Embedded Database for .NET 10**
  
  [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
  [![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/download)
  [![NuGet](https://img.shields.io/badge/NuGet-1.0.4-blue.svg)](https://www.nuget.org/packages/SharpCoreDB)
  [![Sponsor](https://img.shields.io/badge/Sponsor-❤️-ea4aaa?logo=githubsponsors&logoColor=white)](https://github.com/sponsors/mpcoredeveloper)
</div>

---

## 📊 Project Status

**Current Version**: 1.0.2  
**Feature Completion**: 82% ✅  
**Status**: Production-ready for core features

👉 **[View Detailed Status](SharpCoreDB/docs/STATUS.md)** | **[View Roadmap](SharpCoreDB/docs/ROADMAP_2026.md)** | **[Known Issues](SharpCoreDB/docs/KNOWN_ISSUES.md)**

---

A high-performance, encrypted, embedded database engine for .NET 10 with **B-tree indexes**, **SIMD-accelerated analytics**, and **345x analytics speedup**. Pure .NET implementation with enterprise-grade encryption and world-class analytics performance.

- **License**: MIT
- **Platform**: .NET 10, C# 14
- **Encryption**: AES-256-GCM at rest (**0% overhead, sometimes faster!** :white_check_mark:)
- **Analytics**: **345x faster** than LiteDB with SIMD vectorization :white_check_mark:
- **Analytics**: **11.5x faster** than SQLite with SIMD vectorization :white_check_mark:
- **B-tree Indexes**: O(log n + k) range scans, ORDER BY, BETWEEN support :white_check_mark:

### :card_file_box: **SQL Support**

- **DDL**: CREATE TABLE, DROP TABLE, CREATE INDEX, DROP INDEX
- **DML**: INSERT, SELECT, UPDATE, DELETE, INSERT BATCH
- **Queries**: WHERE, ORDER BY, LIMIT, OFFSET, BETWEEN
- **Aggregates**: COUNT, SUM, AVG, MIN, MAX, GROUP BY
- **Constraints**: NOT NULL, UNIQUE, DEFAULT values, CHECK constraints
- **Advanced**: JOINs, subqueries, complex expressions

## :bar_chart: Performance Benchmarks (January 2026)
