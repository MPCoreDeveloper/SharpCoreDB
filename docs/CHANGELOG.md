# Changelog

All notable changes to SharpCoreDB will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Restructured project to standard layout (src/, tests/, tools/)
- Added comprehensive GitHub Actions CI/CD pipeline
- Added Directory.Build.props for shared project properties
- Added .editorconfig for consistent code style
- Added contribution guidelines (CONTRIBUTING.md)
- Added changelog (CHANGELOG.md)

### Changed
- Updated solution file with organized project structure
- Enhanced .gitignore with additional patterns

## [1.0.0] - 2025-01-XX

### Added
- Initial release of SharpCoreDB
- High-performance embedded database engine for .NET 10
- AES-256-GCM encryption at rest with 0% overhead
- SIMD-accelerated analytics (AVX-512, AVX2, SSE2)
  - 345x faster than LiteDB for aggregations
  - 11.5x faster than SQLite for GROUP BY operations
- Multiple storage engines:
  - PageBased (OLTP workloads)
  - Columnar (Analytics workloads)
  - AppendOnly (Logging workloads)
- Dual index types:
  - Hash indexes (O(1) point lookups)
  - B-tree indexes (O(log n) range queries, ORDER BY)
- StructRow API for zero-copy query results
- Comprehensive SQL support:
  - DDL: CREATE TABLE, DROP TABLE, CREATE INDEX, DROP INDEX
  - DML: INSERT, SELECT, UPDATE, DELETE, INSERT BATCH
  - Queries: WHERE, ORDER BY, LIMIT, OFFSET, BETWEEN
  - Aggregates: COUNT, SUM, AVG, MIN, MAX, GROUP BY
- Batch update API for high-throughput operations
- ADO.NET provider (SharpCoreDB.Data.Provider)
- Entity Framework Core provider (SharpCoreDB.EntityFrameworkCore)
- Serilog sink (SharpCoreDB.Serilog.Sinks)
- Extension methods library (SharpCoreDB.Extensions)
- Comprehensive test suite
- Performance benchmarks with BenchmarkDotNet
- Demo application and database viewer
- Full async/await support
- Dependency injection integration
- Pure .NET implementation (no P/Invoke)

### Performance Highlights
- **Analytics**: 345x faster than LiteDB, 11.5x faster than SQLite
- **Batch Updates**: 1.54x faster than LiteDB, 3x less memory
- **Inserts**: 2.1x faster than LiteDB, 6.2x less memory
- **Memory Efficiency**: 10x less memory with StructRow API
- **Encryption**: 0% overhead (sometimes faster!)

### Documentation
- Comprehensive README with benchmarks and examples
- API documentation (XML comments)
- Contributing guidelines
- MIT License

---

## Version History

### Version Format
- **Major.Minor.Patch** (e.g., 1.0.0)
- **Major**: Breaking API changes
- **Minor**: New features, backward compatible
- **Patch**: Bug fixes, backward compatible

### Release Tags
- `v1.0.0` - Initial release

---

## Links
- [GitHub Repository](https://github.com/MPCoreDeveloper/SharpCoreDB)
- [NuGet Package](https://www.nuget.org/packages/SharpCoreDB)
- [Documentation](https://github.com/MPCoreDeveloper/SharpCoreDB#readme)
- [Issue Tracker](https://github.com/MPCoreDeveloper/SharpCoreDB/issues)
