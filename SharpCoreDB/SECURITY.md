# SharpCoreDB Security Documentation

## Overview
This document outlines security considerations, best practices, and known limitations for SharpCoreDB.

## Critical Security Fixes Applied (2026)

### 1. User Password Storage (UserService.cs)
**FIXED**: Hardcoded username-as-salt vulnerability
- **Previous Issue**: Used username as salt for PBKDF2, allowing precomputed rainbow table attacks
- **Fix Applied**: Now generates cryptographically random 16-byte (128-bit) salt per user
- **Impact**: Prevents rainbow table attacks and ensures each password hash is unique
- **Breaking Change**: YES - existing user databases must be migrated

### 2. PBKDF2 Iteration Count (CryptoService.cs)
**FIXED**: Dangerously low iteration count
- **Previous Issue**: Used only 10,000 PBKDF2-HMAC-SHA256 iterations
- **Fix Applied**: Increased to 600,000 iterations per OWASP/NIST 2025 recommendations
- **Impact**: Significantly increases resistance to GPU-accelerated brute force attacks
- **Performance**: ~60x slower key derivation (expected and necessary)
- **Breaking Change**: YES - existing derived keys will not match

**Reference**: [OWASP Password Storage Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html)

### 3. GroupCommitWAL Race Condition
**FIXED**: Race condition in batch collection
- **Previous Issue**: Timing window in batch collection could cause commits to be dropped
- **Fix Applied**: Proper timeout handling with linked cancellation tokens
- **Impact**: Ensures all commits are processed reliably under concurrent load

### 4. GroupCommitWAL ClearAsync Design Flaw
**DOCUMENTED**: ClearAsync cannot properly restart WAL
- **Issue**: Method leaves WAL in broken state (channel/CTS cannot be recreated)
- **Mitigation**: Method now throws exception with clear guidance
- **Recommended Pattern**: Create new GroupCommitWAL instance after checkpoint

## Encryption Architecture

### AES-256-GCM
SharpCoreDB uses AES-256-GCM (Galois/Counter Mode) for data encryption:
- **Key Size**: 256 bits (32 bytes)
- **Nonce Size**: 96 bits (12 bytes) - randomly generated per encryption
- **Tag Size**: 128 bits (16 bytes) - provides authenticated encryption
- **Thread Safety**: Each encryption operation creates a new `AesGcm` instance (no shared state)

**SECURITY NOTES**:
1. **Nonce Uniqueness**: Each encryption uses a cryptographically random nonce from `RandomNumberGenerator.Fill()`
2. **No Nonce Reuse**: Never encrypt more than 2^32 messages with the same key (GCM limit)
3. **Key Reuse**: The same master key is used for all encryptions - see Key Rotation section

### Current Limitations

#### 1. No Key Rotation Support
**CRITICAL LIMITATION**: SharpCoreDB does not support key rotation
- Master key is derived once at database initialization
- No mechanism to re-encrypt data with a new key
- **Risk**: Long-lived databases may exceed GCM nonce space (2^32 operations)
- **Mitigation**: For production use, implement:
  - Periodic database export/re-import with new master password
  - External key rotation before reaching 2^32 encrypted operations
  - Monitor operation count in production

#### 2. No Forward Secrecy
- Compromise of master key exposes all historical data
- WAL logs are encrypted with same key as main database
- **Mitigation**: 
  - Protect master password with HSM or key management service
  - Rotate master password periodically via export/import
  - Consider envelope encryption for sensitive applications

#### 3. Master Key Derivation
- Master key derived from password using PBKDF2-HMAC-SHA256
- Salt is currently hardcoded as string "salt" (should be random per database)
- **TODO**: Generate and store random salt per database file

## SQL Features and Security

### Enhanced SQL Parser & Dialect Support ✅
SharpCoreDB includes a comprehensive SQL parser with multi-dialect support:

#### Supported SQL Dialects:
- **SharpCoreDB** (native dialect with ULID support)
- **SQLite** (compatible with SQLite syntax)
- **PostgreSQL** (function translation and syntax support)
- **MySQL** (MySQL-specific syntax and functions)
- **SQL Server** (T-SQL compatibility)
- **Standard SQL** (ANSI SQL baseline)

#### Parser Capabilities:
✅ **Basic SQL Operations**:
- SELECT, INSERT, UPDATE, DELETE statements
- WHERE, GROUP BY, HAVING, ORDER BY clauses
- LIMIT/OFFSET pagination

✅ **Join Support**:
- INNER JOIN, LEFT JOIN, RIGHT JOIN, FULL OUTER JOIN
- Cross joins and self-joins
- Multiple table joins

✅ **Advanced Features**:
- Subqueries in FROM and WHERE clauses
- Common Table Expressions (CTEs) with dialect support
- Window functions (dialect-dependent)
- IN expressions with subquery support
- COALESCE and aggregate functions

✅ **Error Recovery**:
- Graceful handling of malformed SQL
- Error reporting without crashes
- Partial parsing for analysis tools

#### Dialect-Specific Features:
- **Function Translation**: Automatic translation of functions between dialects (e.g., `LEN` → `LENGTH` for PostgreSQL)
- **Syntax Adaptation**: LIMIT/OFFSET formatting per dialect (e.g., SQL Server uses TOP/OFFSET FETCH)
- **Identifier Quoting**: Proper identifier escaping per dialect (backticks, brackets, double quotes)

### SQL Injection Protection

#### Current State: PARAMETERIZED QUERIES REQUIRED
SharpCoreDB's `SqlParser` provides protection when used correctly:

#### Safe Patterns (Use These) ✅:
```csharp
// GOOD: Use parameterized queries
var params = new Dictionary<string, object?> 
{
    ["@username"] = userInput,
    ["@age"] = age
};
db.ExecuteSQL("INSERT INTO users (name, age) VALUES (@username, @age)", params);

// GOOD: Positional parameters
db.ExecuteSQL("SELECT * FROM users WHERE id = ?", new Dictionary<string, object?> { ["0"] = userId });
```

#### Unsafe Patterns (NEVER USE) ❌:
```csharp
// DANGEROUS: String interpolation
db.ExecuteSQL($"SELECT * FROM users WHERE name = '{userName}'");

// DANGEROUS: String concatenation
db.ExecuteSQL("DELETE FROM users WHERE id = " + userId);
```

#### Protection Mechanisms:
- **Parameter Binding**: All user input must use parameterized queries
- **Runtime Warnings**: Warns when executing SQL without parameters (development)
- **Error Recovery**: Parser continues gracefully on malformed input
- **Dialect Validation**: SQL is validated against target dialect capabilities

### Recommendations for Production:
1. **Always use parameterized queries** for any user input
2. **Never concatenate user input** into SQL strings
3. **Validate input types** before passing to database
4. Consider using an ORM layer (EF Core support available)
5. **Leverage dialect support** for cross-database compatibility

## Concurrency & Thread Safety

### AesGcmEncryption
✅ **THREAD-SAFE**: Each encryption/decryption creates a new `AesGcm` instance
- No shared mutable state
- Per-call instance strategy prevents race conditions
- ArrayPool usage is thread-safe
- Verified by `AesGcmConcurrencyTests` (100+ parallel operations)

### GroupCommitWAL
✅ **THREAD-SAFE**: Properly synchronized with Channel-based queue
- Fixed race condition in batch collection (2026)
- Single-reader (background worker) / multi-writer pattern
- Atomic operations for statistics
- Instance-specific WAL files prevent file locking conflicts

**LIMITATION**: ClearAsync is NOT safe for production use (see above)

### PageCache
✅ **LOCK-FREE**: Uses Interlocked operations for CLOCK eviction
- No locks in hot path
- Lock-free page frame management
- Verified thread-safe under concurrent access

### Database Class
⚠️ **PARTIALLY THREAD-SAFE**:
- WAL operations protected by `_walLock`
- Metadata updates serialized
- Individual table operations may have race conditions
- **Recommendation**: Use one Database instance per thread or external synchronization

## File Handling Security

### Memory-Mapped Files
- Used for large files (>10MB) when `UseMemoryMapping = true`
- Improves read performance by 30-50%
- **Security Note**: Memory-mapped regions are not encrypted in memory
- Data is encrypted at rest but plaintext in memory during access

### WAL File Cleanup
- Instance-specific WAL files (prevents conflicts)
- Automatically deleted on Dispose
- Orphaned file cleanup with `CleanupOrphanedWAL()`
- **TODO**: Secure deletion (overwrite before delete)

## Known Security Issues (Not Fixed)

### 1. Plaintext in Memory
- Decrypted data exists in memory during processing
- No memory protection (MemoryBarrier, SecureString, etc.)
- Vulnerable to memory dump attacks
- **Mitigation**: Use full-disk encryption, secure boot, memory encryption (Intel TME/SGX)

### 2. No Audit Logging
- No built-in audit trail for security events
- Failed login attempts not logged
- Data access not tracked
- **Mitigation**: Implement application-layer audit logging

### 3. No Rate Limiting
- No protection against brute force login attempts
- No connection rate limiting
- **Mitigation**: Implement at application layer

### 4. Metadata Not Encrypted
- Table schemas stored in `meta.json` may not be fully encrypted
- File names reveal table names
- **Mitigation**: Encrypt entire database directory

## Recommended Security Practices

### For Production Deployments:

1. **Master Password Management**
   - Use strong passwords (≥20 characters, random)
   - Store in secure key vault (Azure Key Vault, AWS KMS, HashiCorp Vault)
   - Never hardcode in source code
   - Rotate periodically (export/import pattern)

2. **Network Security**
   - Run database in trusted environment only
   - Use TLS for any network access
   - Implement authentication at application layer

3. **File System Security**
   - Restrict database directory permissions (chmod 700)
   - Enable full-disk encryption
   - Use secure deletion for old database files

4. **Input Validation**
   - Always use parameterized queries
   - Validate all user input at application layer
   - Implement allowlist for table/column names

5. **Monitoring**
   - Monitor encryption operation count (stay below 2^32)
   - Log security events at application layer
   - Alert on suspicious patterns

6. **Backup Security**
   - Encrypt backups separately
   - Test restore procedures
   - Rotate backup encryption keys

## Security Testing

### Concurrency Tests
- `AesGcmConcurrencyTests.cs`: 100+ parallel encryption operations
- `GroupCommitWALInstanceTests.cs`: Multiple concurrent instances
- No race conditions detected under stress testing

### Recommendations for Additional Testing:
1. Fuzzing SQL parser with malformed input
2. Memory safety testing (valgrind, Address Sanitizer)
3. Side-channel analysis (timing attacks on crypto operations)
4. Penetration testing against SQL injection
5. Load testing near GCM nonce limit (2^32 operations)

## Vulnerability Reporting

If you discover a security vulnerability in SharpCoreDB:
1. **DO NOT** open a public GitHub issue
2. Email security details to the maintainer privately
3. Allow reasonable time for fix before disclosure
4. Credit will be given for responsible disclosure

## Compliance Considerations

SharpCoreDB's current security posture:
- ❌ NOT suitable for PCI-DSS (no audit logging, key rotation)
- ❌ NOT suitable for HIPAA (no encryption at rest guarantees, audit trail)
- ❌ NOT suitable for GDPR without additional controls (no right to erasure, audit)
- ✅ Suitable for internal tools with proper infrastructure security
- ✅ Suitable for development/testing environments

For compliance-critical applications, consider:
- Using a commercial database with compliance certifications
- Implementing additional security layers
- Regular security audits
- Professional security consulting

## Version History

### v1.0.0 (2025)
- Initial release with AES-256-GCM encryption
- Basic SQL injection protection
- PBKDF2 with 10,000 iterations (INSECURE)
- Username as salt (INSECURE)

### v1.1.0 (2026) - Security Hardening & SQL Enhancements
- 🔒 Fixed: Random salt per user (16 bytes)
- 🔒 Fixed: PBKDF2 iterations increased to 600,000
- 🔒 Fixed: GroupCommitWAL race condition
- 🔒 Documented: Key rotation limitations
- 🔒 Documented: SQL injection risks
- ✨ **NEW**: Enhanced SQL parser with multi-dialect support
- ✨ **NEW**: SQL dialect support (SharpCoreDB, SQLite, PostgreSQL, MySQL, SQL Server, Standard SQL)
- ✨ **NEW**: Advanced SQL features (RIGHT JOIN, FULL OUTER JOIN, subqueries, CTEs)
- ✨ **NEW**: Function translation between dialects
- ✨ **NEW**: Error recovery in SQL parser
- ✨ **NEW**: SQL visitor pattern for AST manipulation
- ⚠️ Breaking changes to password storage

## References

- [OWASP Top 10](https://owasp.org/www-project-top-ten/)
- [NIST Password Guidelines](https://pages.nist.gov/800-63-3/)
- [AES-GCM Security](https://csrc.nist.gov/publications/detail/sp/800-38d/final)
- [SQL Injection Prevention](https://cheatsheetseries.owasp.org/cheatsheets/SQL_Injection_Prevention_Cheat_Sheet.html)
