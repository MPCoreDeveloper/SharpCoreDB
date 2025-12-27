# Changelog

All notable changes to SharpCoreDB.Serilog.Sinks will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2025-01-XX

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
