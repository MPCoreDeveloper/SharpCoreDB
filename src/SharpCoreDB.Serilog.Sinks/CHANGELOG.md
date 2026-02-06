# Changelog

All notable changes to SharpCoreDB.Serilog.Sinks will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.7] - 2025-06-02

### Changed
- **BREAKING**: `SharpCoreDBSink` is now `sealed` — prevents unintended inheritance
- **PERF**: Replaced per-event `ExecuteSQLAsync` loop with `ExecuteBatchSQLAsync` which routes through `InsertBatch` for direct storage engine writes
- **PERF**: Cached INSERT SQL prefix to avoid repeated string interpolation per event
- **PERF**: `StringBuilder` initialized with capacity hint to reduce resizing in hot path
- **SAFETY**: Added `Flush()` call after batch writes to ensure data persistence to disk
- **SAFETY**: Thread-safe table creation using C# 14 `Lock` class with double-check pattern
- **SAFETY**: Added `ConfigureAwait(false)` on all async calls for library deadlock prevention
- **C# 14**: Raw string literals for multi-line CREATE TABLE SQL
- **C# 14**: Collection expressions (`List<string> statements = []`)
- **C# 14**: `Lock` class instead of `object` for synchronization
- Used `ArgumentException.ThrowIfNullOrWhiteSpace` for parameter validation
- Improved XML documentation with `<see cref="..."/>` cross-references

### Removed
- Unnecessary finalizer (`~SharpCoreDBSink`) — class holds no unmanaged resources
- Simplified `Dispose` pattern for sealed class (no `Dispose(bool)` needed)
- Removed `BeginBatchUpdate`/`EndBatchUpdate`/`CancelBatchUpdate` calls — `ExecuteBatchSQLAsync` handles transactions internally

### Fixed
- `_tableCreated` field was not thread-safe — now protected by `Lock` with double-check pattern
- Missing `Flush()` after writes could cause data loss on crash
- Missing `ConfigureAwait(false)` could deadlock in synchronization contexts
- Replaced manual `throw new ArgumentException` with `ArgumentException.ThrowIfNullOrWhiteSpace`

## [1.0.0] - 2025-01-28

### Added
- Initial release of SharpCoreDB.Serilog.Sinks
- PeriodicBatchingSink implementation for efficient batch logging
- Automatic table creation with AppendOnly storage engine
- ULID AUTO primary key for sortable, distributed-friendly IDs
- Full async/await support
- Batch update API integration (`BeginBatchUpdate`/`EndBatchUpdate`)
- Error handling with automatic rollback via `CancelBatchUpdate()`
- JSON serialization for log properties
- Multiple configuration methods:
  - Direct database instance
  - Connection string (path + password)
  - Options object
- Configurable options:
  - Table name
  - Batch size and period
  - Storage engine (AppendOnly/PageBased/Columnar)
  - Auto table creation
- AES-256-GCM encryption support (via SharpCoreDB)
- Service provider integration for dependency injection
- Comprehensive documentation in README.md
- .NET 10 support

### Performance
- 10,000+ logs/second on modern hardware
- Sub-millisecond latency per batch
- Minimal memory footprint
- Optimized for high-volume logging scenarios
- ULID-based sorting (faster than timestamp column sorting)
- B-tree index support for timestamp range queries

### Documentation
- Complete usage examples in README.md (copy/paste ready)
- Query performance tips for ULID vs Timestamp sorting
- Index management guide for production systems
- Performance comparison examples
- Best practices for chronological queries
- B-tree index recommendations for range queries
- Composite index patterns for Level + Timestamp filtering
- ASP.NET Core integration example
- Structured logging examples
- Performance testing examples
- All examples as inline code blocks (no separate Examples class)

### Security
- Built-in AES-256-GCM encryption
- No plaintext log storage
- Secure password-based database access

### Query Optimization
- ULID primary key enables fast chronological sorting without additional indexes
- Recommended B-tree index on Timestamp for range queries (`WHERE Timestamp BETWEEN`)
- Composite index pattern for Level + Timestamp queries
- Performance comparison showing ULID sorting is faster than Timestamp column sorting
- Example queries demonstrating optimal index usage

### Project Structure
- Clean library project (no example classes in production code)
- All examples in README.md as documentation
- Follows .NET library best practices
- NuGet-ready package structure

[1.0.0]: https://github.com/MPCoreDeveloper/SharpCoreDB/releases/tag/v1.0.0
