<div align="center">
  
  # SharpCoreDB
  
  <img src="https://raw.githubusercontent.com/MPCoreDeveloper/SharpCoreDB/master/SharpCoreDB.jpg" alt="SharpCoreDB Logo" width="300"/>
  
  **High-Performance Embedded Database for .NET 10**
  
  [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
  [![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/download)
  [![NuGet](https://img.shields.io/badge/NuGet-1.0.0-blue.svg)](https://www.nuget.org/packages/SharpCoreDB)
  
</div>

---

A high-performance, encrypted, embedded database engine for .NET 10 with **B-tree indexes**, **SIMD-accelerated analytics**, and **345x analytics speedup**. Pure .NET implementation with enterprise-grade encryption and world-class analytics performance.

- **License**: MIT
- **Platform**: .NET 10, C# 14
- **Encryption**: AES-256-GCM at rest (**0% overhead, sometimes faster!** :white_check_mark:)
- **Analytics**: **345x faster** than LiteDB with SIMD vectorization :white_check_mark:
- **Analytics**: **11.5x faster** than SQLite with SIMD vectorization :white_check_mark:
- **B-tree Indexes**: O(log n + k) range scans, ORDER BY, BETWEEN support :white_check_mark:
