# 🎯 Phase 11 Update: gRPC as First-Class Citizen

## Protocol Priority Revision

### OLD (Incorrect)
```
Primary:   Binary Protocol (PostgreSQL-inspired)
Secondary: HTTP REST API
Optional:  gRPC
```

### NEW (Correct) ✅
```
PRIMARY:   gRPC Protocol (Protobuf, HTTP/2)
SECONDARY: Binary Protocol (PostgreSQL compatibility)
TERTIARY:  HTTP REST API (Web browsers, simple clients)
```

---

## Why gRPC First?

### Performance Advantages
1. **Binary Serialization** - Protobuf is 3-10x smaller than JSON
2. **HTTP/2 Multiplexing** - Multiple requests on single connection
3. **Bidirectional Streaming** - Real-time result streaming
4. **Type Safety** - Strong typing, code generation
5. **Cross-Platform** - Native clients for all major languages

### Industry Adoption
- ✅ Google Cloud (all services)
- ✅ Netflix (internal microservices)
- ✅ Uber (core platform)
- ✅ Square (payment processing)
- ✅ CNCF (Cloud Native Computing Foundation standard)

### Developer Experience
- ✅ **Code Generation** - Client libraries auto-generated from `.proto`
- ✅ **Tooling** - gRPCurl, Postman, BloomRPC
- ✅ **Documentation** - Built-in via Protobuf comments
- ✅ **Versioning** - Proto3 backward compatibility

---

## Architecture Update

### Server Stack (Updated)
```
┌─────────────────────────────────────────────────────────────┐
│  SharpCoreDB.Server (Main Executable)                        │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  ASP.NET Core 10 (Kestrel Web Server)                │   │
│  │                                                        │   │
│  │  PRIMARY:   gRPC Service (Port 50051)                 │   │
│  │             ├─ ExecuteQuery (streaming)               │   │
│  │             ├─ ExecuteNonQuery                        │   │
│  │             ├─ PrepareStatement                       │   │
│  │             └─ BeginTransaction                       │   │
│  │                                                        │   │
│  │  SECONDARY: Binary Protocol Handler (Port 5433)       │   │
│  │             └─ PostgreSQL wire protocol compatibility │   │
│  │                                                        │   │
│  │  TERTIARY:  HTTP REST API (Port 8080)                 │   │
│  │             └─ JSON/WebSocket for browsers            │   │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

---

## gRPC Service Definition (Complete)

### `sharpcoredb.proto`
```protobuf
syntax = "proto3";
package sharpcoredb.v1;

option csharp_namespace = "SharpCoreDB.Server.Protocol.Grpc";

// ================================================================
// MAIN SERVICE
// ================================================================
service SharpCoreDBService {
  // Connection lifecycle
  rpc Connect(ConnectRequest) returns (ConnectResponse);
  rpc Disconnect(DisconnectRequest) returns (DisconnectResponse);
  rpc Ping(PingRequest) returns (PingResponse);
  
  // Query execution (PRIMARY USE CASE)
  rpc ExecuteQuery(QueryRequest) returns (stream QueryResponse);
  rpc ExecuteNonQuery(NonQueryRequest) returns (NonQueryResponse);
  rpc ExecuteBatch(BatchRequest) returns (BatchResponse);
  
  // Prepared statements
  rpc PrepareStatement(PrepareRequest) returns (PrepareResponse);
  rpc ExecutePrepared(ExecutePreparedRequest) returns (stream QueryResponse);
  rpc ClosePrepared(ClosePreparedRequest) returns (ClosePreparedResponse);
  
  // Transactions
  rpc BeginTransaction(BeginTxRequest) returns (BeginTxResponse);
  rpc CommitTransaction(CommitTxRequest) returns (CommitTxResponse);
  rpc RollbackTransaction(RollbackTxRequest) returns (RollbackTxResponse);
  
  // Metadata & introspection
  rpc GetSchema(GetSchemaRequest) returns (GetSchemaResponse);
  rpc GetServerInfo(GetServerInfoRequest) returns (GetServerInfoResponse);
  rpc GetStatistics(GetStatisticsRequest) returns (GetStatisticsResponse);
  
  // Vector search (SharpCoreDB-specific)
  rpc VectorSearch(VectorSearchRequest) returns (stream VectorSearchResponse);
  rpc BuildVectorIndex(BuildVectorIndexRequest) returns (BuildVectorIndexResponse);
}

// ================================================================
// CONNECTION MESSAGES
// ================================================================
message ConnectRequest {
  string database = 1;
  string username = 2;
  string password = 3;
  map<string, string> options = 4;  // e.g., {"isolation_level": "serializable"}
  string client_version = 5;
}

message ConnectResponse {
  string session_id = 1;
  string server_version = 2;
  repeated string capabilities = 3;  // e.g., ["transactions", "vector_search"]
  int64 connection_id = 4;
}

message DisconnectRequest {
  string session_id = 1;
}

message DisconnectResponse {
  bool success = 1;
}

message PingRequest {
  string session_id = 1;
}

message PingResponse {
  int64 server_time_ms = 1;
}

// ================================================================
// QUERY EXECUTION MESSAGES
// ================================================================
message QueryRequest {
  string session_id = 1;
  string sql = 2;
  repeated Parameter parameters = 3;
  int32 max_rows = 4;  // 0 = unlimited
  int32 fetch_size = 5;  // Rows per chunk (default: 1000)
  bool include_metadata = 6;  // Column types, table names
}

message QueryResponse {
  oneof response {
    RowSet row_set = 1;
    ExecutionStats stats = 2;
    ErrorInfo error = 3;
  }
}

message RowSet {
  repeated ColumnMetadata columns = 1;  // Only sent in first chunk
  repeated Row rows = 2;
  bool has_more = 3;  // True if more chunks follow
  int64 total_rows = 4;  // -1 if unknown
}

message Row {
  repeated Value values = 1;
}

message Value {
  oneof value {
    int64 int_value = 1;
    double double_value = 2;
    string string_value = 3;
    bool bool_value = 4;
    bytes bytes_value = 5;
    google.protobuf.Timestamp timestamp_value = 6;
    DecimalValue decimal_value = 7;
    VectorValue vector_value = 8;
  }
  bool is_null = 9;
}

message DecimalValue {
  int64 unscaled_value = 1;
  int32 scale = 2;
}

message VectorValue {
  repeated float values = 1;
}

message NonQueryRequest {
  string session_id = 1;
  string sql = 2;
  repeated Parameter parameters = 3;
}

message NonQueryResponse {
  int64 rows_affected = 1;
  ExecutionStats stats = 2;
  ErrorInfo error = 3;
}

message BatchRequest {
  string session_id = 1;
  repeated string statements = 2;
  repeated ParameterSet parameter_sets = 3;
}

message BatchResponse {
  repeated int64 rows_affected = 1;
  ExecutionStats stats = 2;
  repeated ErrorInfo errors = 3;  // Per-statement errors
}

message Parameter {
  string name = 1;  // e.g., "age" for :age or @age
  Value value = 2;
}

message ParameterSet {
  repeated Parameter parameters = 1;
}

// ================================================================
// PREPARED STATEMENTS
// ================================================================
message PrepareRequest {
  string session_id = 1;
  string sql = 2;
}

message PrepareResponse {
  string statement_id = 1;
  int32 parameter_count = 2;
  repeated ColumnMetadata columns = 3;
}

message ExecutePreparedRequest {
  string session_id = 1;
  string statement_id = 2;
  repeated Parameter parameters = 3;
}

message ClosePreparedRequest {
  string session_id = 1;
  string statement_id = 2;
}

message ClosePreparedResponse {
  bool success = 1;
}

// ================================================================
// TRANSACTIONS
// ================================================================
message BeginTxRequest {
  string session_id = 1;
  IsolationLevel isolation_level = 2;
  bool read_only = 3;
}

message BeginTxResponse {
  string transaction_id = 1;
}

message CommitTxRequest {
  string session_id = 1;
  string transaction_id = 2;
}

message CommitTxResponse {
  bool success = 1;
  ErrorInfo error = 2;
}

message RollbackTxRequest {
  string session_id = 1;
  string transaction_id = 2;
}

message RollbackTxResponse {
  bool success = 1;
}

enum IsolationLevel {
  READ_UNCOMMITTED = 0;
  READ_COMMITTED = 1;
  REPEATABLE_READ = 2;
  SERIALIZABLE = 3;
}

// ================================================================
// METADATA
// ================================================================
message GetSchemaRequest {
  string session_id = 1;
  string database = 2;
  string table_pattern = 3;  // SQL LIKE pattern, e.g., "user%"
}

message GetSchemaResponse {
  repeated TableMetadata tables = 1;
}

message TableMetadata {
  string name = 1;
  string schema = 2;
  repeated ColumnMetadata columns = 3;
  repeated IndexMetadata indexes = 4;
}

message ColumnMetadata {
  string name = 1;
  DataType type = 2;
  bool nullable = 3;
  bool primary_key = 4;
  string default_value = 5;
}

message IndexMetadata {
  string name = 1;
  repeated string columns = 2;
  IndexType type = 3;
  bool unique = 4;
}

enum IndexType {
  BTREE = 0;
  HASH = 1;
  VECTOR_HNSW = 2;
}

message GetServerInfoRequest {
  string session_id = 1;
}

message GetServerInfoResponse {
  string version = 1;
  string build_date = 2;
  string platform = 3;
  map<string, string> capabilities = 4;
  ServerStatistics statistics = 5;
}

message GetStatisticsRequest {
  string session_id = 1;
}

message GetStatisticsResponse {
  ServerStatistics statistics = 1;
}

message ServerStatistics {
  int64 uptime_seconds = 1;
  int64 active_connections = 2;
  int64 total_queries = 3;
  int64 cache_hit_rate = 4;  // Percentage (0-100)
  int64 memory_usage_bytes = 5;
  double cpu_usage_percent = 6;
}

// ================================================================
// VECTOR SEARCH (SharpCoreDB-specific)
// ================================================================
message VectorSearchRequest {
  string session_id = 1;
  string table = 2;
  string vector_column = 3;
  repeated float query_vector = 4;
  int32 k = 5;  // Top-K results
  DistanceMetric metric = 6;
  string filter = 7;  // Optional WHERE clause
}

message VectorSearchResponse {
  repeated VectorSearchResult results = 1;
  ExecutionStats stats = 2;
}

message VectorSearchResult {
  int64 row_id = 1;
  float distance = 2;
  map<string, Value> columns = 3;  // Other columns
}

enum DistanceMetric {
  COSINE = 0;
  EUCLIDEAN = 1;
  MANHATTAN = 2;
  DOT_PRODUCT = 3;
}

message BuildVectorIndexRequest {
  string session_id = 1;
  string table = 2;
  string vector_column = 3;
  HNSWParameters params = 4;
}

message BuildVectorIndexResponse {
  bool success = 1;
  int64 indexed_rows = 2;
  double build_time_seconds = 3;
}

message HNSWParameters {
  int32 m = 1;  // Max connections per layer
  int32 ef_construction = 2;  // Build-time search width
  int32 ef_search = 3;  // Query-time search width
}

// ================================================================
// COMMON TYPES
// ================================================================
enum DataType {
  INTEGER = 0;
  REAL = 1;
  TEXT = 2;
  BOOLEAN = 3;
  DATETIME = 4;
  BLOB = 5;
  DECIMAL = 6;
  VECTOR = 7;
}

message ExecutionStats {
  double execution_time_ms = 1;
  int64 rows_scanned = 2;
  int64 rows_returned = 3;
  int64 bytes_transferred = 4;
  string query_plan = 5;  // JSON execution plan
}

message ErrorInfo {
  int32 code = 1;  // Error code
  string message = 2;
  string sql_state = 3;  // SQL standard error code
  string detail = 4;  // Additional context
  string hint = 5;  // Suggestion for fix
}

import "google/protobuf/timestamp.proto";
```

---

## Client Example (C# gRPC)

### Installation
```bash
dotnet add package SharpCoreDB.Client.Grpc
```

### Usage
```csharp
using SharpCoreDB.Client.Grpc;
using Grpc.Core;
using Grpc.Net.Client;

// Create gRPC channel
var channel = GrpcChannel.ForAddress("https://localhost:50051");
var client = new SharpCoreDBService.SharpCoreDBServiceClient(channel);

// Connect
var connectResponse = await client.ConnectAsync(new ConnectRequest
{
    Database = "mydb",
    Username = "admin",
    Password = "secret",
    Options = { { "isolation_level", "serializable" } }
});

var sessionId = connectResponse.SessionId;
Console.WriteLine($"Connected! Session: {sessionId}");

// Execute query with streaming
var queryRequest = new QueryRequest
{
    SessionId = sessionId,
    Sql = "SELECT * FROM users WHERE age > :age",
    Parameters = {
        new Parameter { Name = "age", Value = new Value { IntValue = 25 } }
    },
    FetchSize = 1000  // Rows per chunk
};

using var streamingCall = client.ExecuteQuery(queryRequest);

int rowCount = 0;
await foreach (var response in streamingCall.ResponseStream.ReadAllAsync())
{
    if (response.ResponseCase == QueryResponse.ResponseOneofCase.RowSet)
    {
        foreach (var row in response.RowSet.Rows)
        {
            var id = row.Values[0].IntValue;
            var name = row.Values[1].StringValue;
            var age = row.Values[2].IntValue;
            
            Console.WriteLine($"User: {id}, {name}, {age}");
            rowCount++;
        }
        
        if (!response.RowSet.HasMore)
        {
            Console.WriteLine($"Total rows: {rowCount}");
            break;
        }
    }
    else if (response.ResponseCase == QueryResponse.ResponseOneofCase.Error)
    {
        Console.WriteLine($"Error: {response.Error.Message}");
        break;
    }
}

// Disconnect
await client.DisconnectAsync(new DisconnectRequest { SessionId = sessionId });
```

---

## Timeline Update

### Week 7: gRPC Protocol (PRIMARY - MOST IMPORTANT) ⭐
**Goal:** Production-ready gRPC service with full SharpCoreDB capabilities

**Deliverables:**
- ✅ `sharpcoredb.proto` complete (all services)
- ✅ gRPC service implementation
- ✅ Streaming query responses
- ✅ Prepared statements
- ✅ Transaction support
- ✅ Vector search operations
- ✅ Error handling & retries
- ✅ gRPC health checks
- ✅ Reflection service (gRPCurl support)

**Files:**
- `src/SharpCoreDB.Server.Protocol.Grpc/sharpcoredb.proto`
- `src/SharpCoreDB.Server.Protocol.Grpc/SharpCoreDBServiceImpl.cs`
- `src/SharpCoreDB.Server.Protocol.Grpc/GrpcErrorMapper.cs`
- `tests/SharpCoreDB.Server.Tests/GrpcProtocolTests.cs`

---

### Week 8: Binary Protocol (SECONDARY - PostgreSQL compatibility)
**Goal:** Binary protocol for PostgreSQL client compatibility

**Deliverables:**
- ✅ PostgreSQL wire protocol subset
- ✅ Message framing
- ✅ Query/response handling
- ✅ Prepared statements

---

### Week 11: HTTP REST API (TERTIARY - Browser/simple clients)
**Goal:** REST API for browsers and simple HTTP clients

**Deliverables:**
- ✅ REST endpoints
- ✅ JSON serialization
- ✅ WebSocket streaming
- ✅ Swagger/OpenAPI

---

## Summary of Changes

### Key Updates
1. ✅ **gRPC is now PRIMARY protocol** (not optional)
2. ✅ Complete Protobuf service definition included
3. ✅ Full C# client example with streaming
4. ✅ Vector search operations in gRPC
5. ✅ Week 7 now focuses on gRPC (most critical)
6. ✅ Benchmarks plan includes gRPC performance validation

### Rationale
- **Performance**: gRPC is 3-10x faster than REST
- **Streaming**: Native support for large result sets
- **Type Safety**: Protobuf prevents serialization errors
- **Industry Standard**: Used by Google, Netflix, Uber
- **Tooling**: Excellent debugging, testing tools

---

**Status:** ✅ Updated to prioritize gRPC  
**Approved By:** Architecture Team  
**Date:** 2025-01-28
