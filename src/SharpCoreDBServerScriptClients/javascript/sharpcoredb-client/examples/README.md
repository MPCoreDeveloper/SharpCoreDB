# @sharpcoredb/client Examples

This directory contains examples demonstrating how to use @sharpcoredb/client to connect to and interact with SharpCoreDB Server.


## Patch updates in v1.8.0

- ✅ Aligned package metadata and version references to the synchronized 1.8.0 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.

## Features demonstrated

- Server connection and query execution
- Basic CRUD command flows
- Connection pooling patterns
- Error handling and latency checks

## Changes in v1.8.0

- Example docs aligned to the current `v1.8.0` server/client release line
- References updated for gRPC-first server usage

## Prerequisites

Before running the examples, ensure you have:

1. **SharpCoreDB Server running** on `localhost:5001` (gRPC) or `localhost:8443` (HTTP/WebSocket)
2. **Node.js 16+** installed
3. **@sharpcoredb/client** installed:
   ```bash
   cd clients/javascript/sharpcoredb-client
   npm install
   npm run build
   ```

## Examples

### 1. Basic Connection (`basic-example.js`)

Demonstrates fundamental database operations:
- Connecting to SharpCoreDB Server
- Creating tables
- Inserting and querying data
- Error handling

```bash
node examples/basic-example.js
```

### 2. Connection Pooling (`pooling-example.js`)

Shows how to use connection pooling for high-performance applications:
- Creating a connection pool
- Concurrent worker tasks
- Pool statistics and monitoring
- Performance measurements

```bash
node examples/pooling-example.js
```

## Running Examples

1. **Start SharpCoreDB Server** (see main documentation)
2. **Build the client**:
   ```bash
   cd clients/javascript/sharpcoredb-client
   npm run build
   ```
3. **Run examples**:
   ```bash
   node examples/basic-example.js
   node examples/pooling-example.js
   ```

## Example Output

### Basic Example
```
🚀 @sharpcoredb/client Basic Example
=====================================
📡 Connecting to database...
✅ Connected successfully
📝 Creating test table...
✅ Table created
📥 Inserting test data...
✅ Inserted 1 row(s)
📤 Querying data...
✅ Found 1 user(s) aged 25+:
  1. Alice (30 years old)
🏓 Pinging server...
✅ Server latency: 1.23ms
🔌 Closing connection...
✅ Connection closed

🎉 Example completed successfully!
```

### Pooling Example
```
🏊 @sharpcoredb/client Pooling Example
======================================
🏗️  Creating connection pool...
✅ Pool created
   Max connections: 10
   Min connections: 2
   Initial stats: {"available":0,"inUse":0,"totalCreated":0,"totalDestroyed":0,"maxConnections":10}

🚀 Starting 5 workers with 10 tasks each...
👷 Worker 0: Completed 10 tasks
👷 Worker 1: Completed 10 tasks
👷 Worker 2: Completed 10 tasks
👷 Worker 3: Completed 10 tasks
👷 Worker 4: Completed 10 tasks

📊 Final pool stats: {"available":5,"inUse":0,"totalCreated":5,"totalDestroyed":0,"maxConnections":10}

🎯 Performance Results:
   Total tasks completed: 50
   Total time: 0.75s
   Tasks/second: 66.7
   Avg latency: ~25ms per task

🔌 Closing pool...
✅ Pool closed

🎉 Pooling example completed successfully!
```

## Configuration

Examples use default connection settings. To customize:

```javascript
// Custom connection
const connection = await connect('grpc://your-server:5001', {
  database: 'yourdb',
  username: 'user',
  password: 'pass',
  timeout: 5000
});

// Custom pool
const pool = await createPool('your-server', 5001, {
  database: 'yourdb',
  maxConnections: 20,
  acquireTimeout: 15000
});
```

## Troubleshooting

### Connection Errors
- Ensure SharpCoreDB Server is running
- Check firewall settings (ports 5001/8443)
- Verify TLS certificates if using HTTPS

### Import Errors
- Build the client first: `npm run build`
- Check that `dist/index.js` exists

### Protocol Errors
- Try different protocols: `grpc://`, `https://`, `ws://`
- Check server logs for connection attempts

## Next Steps

- Read the main [README.md](../README.md) for API reference
- Check [test files](../tests/) for more usage examples
- Visit [SharpCoreDB documentation](https://sharpcoredb.com/docs) for advanced topics

