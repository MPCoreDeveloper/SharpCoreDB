# SharpCoreDB Binary Protocol Specification (v1.7.0)

**Protocol Version:** 1.0  
**Compatible With:** PostgreSQL Wire Protocol (extended)  
**Transport:** TCP/IP with TLS 1.2+  
**Default Port:** 5433  

---

## Overview

The SharpCoreDB binary protocol provides PostgreSQL-compatible wire protocol support for high-performance database access. It extends the PostgreSQL protocol with SharpCoreDB-specific features like vector search and graph operations.

### Key Features
- **PostgreSQL Compatible:** Works with existing PostgreSQL clients and drivers
- **High Performance:** Binary serialization, streaming results
- **Secure:** TLS-only, no plain text connections
- **Extensible:** Custom message types for SharpCoreDB features

---

## Message Format

All messages follow the PostgreSQL wire protocol format:

```
┌─────────┬─────────┬──────────────────────┐
│ Type    │ Length  │ Payload              │
│ (1 byte)│ (4 byte)│ (Length - 4 bytes)   │
└─────────┴─────────┴──────────────────────┘
```

- **Type:** ASCII character identifying message type
- **Length:** Total message length in bytes (including length field)
- **Payload:** Message-specific data

---

## Connection Lifecycle

### 1. Startup Message (Client → Server)

```
Type: (no type byte, starts with length)
Length: 4 bytes (total message length)
Protocol Version: 4 bytes (196608 = 3.0)
Parameters: null-terminated key=value pairs
  - user: username
  - database: database name
  - client_encoding: UTF8
  - application_name: client name
  - sharpcoredb_version: client version
```

**Example:**
```
00 00 00 4F 00 03 00 00 75 73 65 72 00 61 64 6D 69 6E 00
64 61 74 61 62 61 73 65 00 6D 79 64 62 00 63 6C 69 65 6E
74 5F 65 6E 63 6F 64 69 6E 67 00 55 54 46 38 00 61 70 70
6C 69 63 61 74 69 6F 6E 5F 6E 61 6D 65 00 53 68 61 72 70
43 6F 72 65 44 42 20 43 6C 69 65 6E 74 00 73 68 61 72 70
63 6F 72 65 64 62 5F 76 65 72 73 69 6F 6E 00 31 2E 35 2E
30 00 00
```

### 2. Authentication Request (Server → Client)

```
Type: 'R'
Length: 4 bytes
Auth Type: 4 bytes
  - 0: AuthenticationOk
  - 3: AuthenticationCleartextPassword
  - 5: AuthenticationMD5Password
  - 10: AuthenticationSASL
  - 12: AuthenticationSASLContinue
  - 13: AuthenticationSASLFinal
```

### 3. Password Message (Client → Server)

```
Type: 'p'
Length: 4 bytes
Password: null-terminated string
```

### 4. Authentication OK (Server → Client)

```
Type: 'R'
Length: 8 bytes
Auth Type: 0 (AuthenticationOk)
```

### 5. Parameter Status (Server → Client)

```
Type: 'S'
Length: 4 bytes
Parameter: null-terminated string (name)
Value: null-terminated string
```

**Common Parameters:**
- server_version: "1.7.0"
- client_encoding: "UTF8"
- server_encoding: "UTF8"
- session_authorization: username
- sharpcoredb_features: "vector,graph,analytics"

### 6. Backend Key Data (Server → Client)

```
Type: 'K'
Length: 12 bytes
Process ID: 4 bytes
Secret Key: 4 bytes
```

### 7. Ready For Query (Server → Client)

```
Type: 'Z'
Length: 5 bytes
Transaction Status: 1 byte
  - 'I': Idle (not in transaction)
  - 'T': In transaction
  - 'E': In failed transaction
```

---

## Query Execution

### 1. Query Message (Client → Server)

```
Type: 'Q'
Length: 4 bytes
Query: null-terminated SQL string
```

### 2. Row Description (Server → Client)

```
Type: 'T'
Length: 4 bytes
Field Count: 2 bytes
Field Data: repeated for each field
  - Field Name: null-terminated string
  - Table OID: 4 bytes (0 for computed columns)
  - Column OID: 4 bytes (0 for computed columns)
  - Data Type OID: 4 bytes (SharpCoreDB type mapping)
  - Data Type Size: 2 bytes (-1 for variable)
  - Type Modifier: 4 bytes
  - Format Code: 2 bytes (0=text, 1=binary)
```

**SharpCoreDB Type OIDs:**
- INTEGER: 23
- LONG: 20
- REAL: 701
- STRING: 25
- BLOB: 17
- BOOLEAN: 16
- DATETIME: 1114
- GUID: 2950
- ULID: 2951
- ROWREF: 2952 (SharpCoreDB extension)
- VECTOR: 2953 (SharpCoreDB extension)
- DECIMAL: 1700

### 3. Data Row (Server → Client)

```
Type: 'D'
Length: 4 bytes
Field Count: 2 bytes
Field Data: repeated for each field
  - Field Length: 4 bytes (-1 for NULL)
  - Field Value: variable length bytes
```

### 4. Command Complete (Server → Client)

```
Type: 'C'
Length: 4 bytes
Command Tag: null-terminated string
  - INSERT: "INSERT oid rows" (e.g., "INSERT 0 5")
  - UPDATE: "UPDATE rows" (e.g., "UPDATE 3")
  - DELETE: "DELETE rows" (e.g., "DELETE 2")
  - SELECT: "SELECT rows" (e.g., "SELECT 100")
```

### 5. Empty Query Response (Server → Client)

```
Type: 'I'
Length: 4 bytes
```

---

## Extended Query Protocol (Prepared Statements)

### 1. Parse Message (Client → Server)

```
Type: 'P'
Length: 4 bytes
Statement Name: null-terminated string
Query: null-terminated string
Parameter Type Count: 2 bytes
Parameter Type OIDs: 4 bytes each
```

### 2. Bind Message (Client → Server)

```
Type: 'B'
Length: 4 bytes
Portal Name: null-terminated string
Statement Name: null-terminated string
Parameter Format Count: 2 bytes
Parameter Formats: 2 bytes each (0=text, 1=binary)
Parameter Count: 2 bytes
Parameter Values: variable (length + data)
Result Format Count: 2 bytes
Result Formats: 2 bytes each (0=text, 1=binary)
```

### 3. Describe Message (Client → Server)

```
Type: 'D'
Length: 4 bytes
Describe Type: 1 byte ('S' for statement, 'P' for portal)
Name: null-terminated string
```

### 4. Execute Message (Client → Server)

```
Type: 'E'
Length: 4 bytes
Portal Name: null-terminated string
Max Rows: 4 bytes (0 = unlimited)
```

### 5. Sync Message (Client → Server)

```
Type: 'S'
Length: 4 bytes
```

### 6. Parse Complete (Server → Client)

```
Type: '1'
Length: 4 bytes
```

### 7. Bind Complete (Server → Client)

```
Type: '2'
Length: 4 bytes
```

### 8. Parameter Description (Server → Client)

```
Type: 't'
Length: 4 bytes
Parameter Count: 2 bytes
Parameter Type OIDs: 4 bytes each
```

---

## Transaction Management

### 1. Begin Transaction

```
Query: 'Q'
SQL: "BEGIN" or "START TRANSACTION [isolation level]"
```

### 2. Commit Transaction

```
Query: 'Q'
SQL: "COMMIT"
```

### 3. Rollback Transaction

```
Query: 'Q'
SQL: "ROLLBACK"
```

### 4. Savepoint

```
Query: 'Q'
SQL: "SAVEPOINT name" / "RELEASE SAVEPOINT name" / "ROLLBACK TO SAVEPOINT name"
```

---

## SharpCoreDB Extensions

### Vector Search Extension

**Custom Query Format:**
```sql
SELECT * FROM vector_search('table_name', 'vector_column', '[1.0,2.0,3.0]', 10, 'cosine');
```

**Result Format:** Standard DataRow messages with vector distance as additional column.

### Graph Operations Extension

**Custom Query Format:**
```sql
SELECT * FROM graph_traverse('start_table', 123, 'relationship_column', 'OUTGOING', 3);
```

**Result Format:** Standard DataRow messages with path information.

### Analytics Extension

**Custom Query Format:**
```sql
SELECT percentile(column, 95) FROM table_name;
```

**Result Format:** Standard DataRow messages with computed analytics values.

---

## Error Handling

### Error Response (Server → Client)

```
Type: 'E'
Length: 4 bytes
Error Fields: repeated null-terminated strings
  - 'S': Severity ('ERROR', 'FATAL', 'PANIC', 'WARNING', 'NOTICE', 'DEBUG', 'INFO', 'LOG')
  - 'C': SQLSTATE code (5 characters)
  - 'M': Message
  - 'D': Detail
  - 'H': Hint
  - 'P': Position (character offset in query)
  - 'p': Internal position
  - 'q': Internal query
  - 'W': Where
  - 'F': File (source code file)
  - 'L': Line (source code line)
  - 'R': Routine (source code function)
```

**Common SQLSTATE Codes:**
- 08003: connection_does_not_exist
- 08006: connection_failure
- 23505: unique_violation
- 23503: foreign_key_violation
- 23502: not_null_violation
- 42703: undefined_column
- 42P01: undefined_table

### Notice Response (Server → Client)

```
Type: 'N'
Length: 4 bytes
Notice Fields: same format as Error Response
```

---

## Connection Termination

### 1. Terminate Message (Client → Server)

```
Type: 'X'
Length: 4 bytes
```

### 2. Connection Close (Server closes connection)

No message sent, connection is closed.

---

## Performance Optimizations

### 1. Binary Format
- Use binary format (format code = 1) for better performance
- Avoid text parsing overhead for numeric types

### 2. Streaming Results
- Server can send partial result sets
- Client can request more data with Execute messages

### 3. Connection Pooling
- Clients should maintain connection pools
- Avoid connection churn for better performance

### 4. Prepared Statements
- Use Parse/Bind/Execute for repeated queries
- Reduces parsing overhead

---

## Security Considerations

### 1. TLS Required
- All connections MUST use TLS 1.2 or higher
- No plain text connections allowed

### 2. Authentication
- Supports multiple authentication methods
- Passwords are hashed using SCRAM-SHA-256

### 3. Authorization
- Row-level security through SQL
- Column-level permissions via views

### 4. Audit Logging
- All queries are logged with session information
- Sensitive data is masked in logs

---

## Implementation Notes

### Client Libraries
- **.NET:** Use `Npgsql` with SharpCoreDB extensions
- **Python:** Use `psycopg2` or `asyncpg`
- **JavaScript:** Use `pg` library
- **Go:** Use `pq` driver

### Testing
- Use PostgreSQL test suites for protocol compliance
- SharpCoreDB-specific features require custom tests

### Monitoring
- Connection count and query statistics
- Protocol-level metrics for performance tuning

---

**Last Updated:** January 28, 2026  
**Protocol Version:** 1.0  
**Compatible Clients:** PostgreSQL-compatible drivers
