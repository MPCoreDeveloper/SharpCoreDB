<!--
MIT License

Copyright (c) 2025 MPCoreDeveloper

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
-->

# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2024-10-01

### Added
- Initial release of SharpCoreDB
- SQL support with CREATE, INSERT, SELECT, UPDATE, DELETE
- AES-256-GCM encryption for data at rest
- Write-Ahead Logging (WAL) for durability
- User authentication and management
- Support for multiple data types: INTEGER, TEXT, REAL, BLOB, BOOLEAN, DATETIME, LONG, DECIMAL, ULID, GUID
- Auto-generated ULID and GUID fields
- Primary key support
- JOIN operations (INNER, LEFT)
- Readonly mode
- Dependency Injection integration
- B-Tree indexing
- Async/await support
- Batch operations
- Connection pooling
- Connection string parsing
- Auto maintenance (VACUUM, WAL checkpointing)
- UPSERT support
- Hash indexes for fast lookups
- EXPLAIN query plans
- Date/time functions: NOW(), DATE(), STRFTIME(), DATEADD()
- Aggregate functions: SUM(), AVG(), COUNT(DISTINCT), GROUP_CONCAT()
- PRAGMA commands for metadata
- Entity Framework Core provider
- Extensions for Dapper and health checks
- NuGet packaging with symbols
- Comprehensive documentation and examples

### Changed
- Optimized for .NET 10 and C# 14

### Deprecated
- None

### Removed
- None

### Fixed
- None

### Security
- AES-256-GCM encryption implementation
