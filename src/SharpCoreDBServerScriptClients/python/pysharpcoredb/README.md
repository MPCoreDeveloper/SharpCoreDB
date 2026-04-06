# PySharpDB

[![PyPI version](https://badge.fury.io/py/pysharpcoredb.svg)](https://pypi.org/project/pysharpcoredb/)
[![Python versions](https://img.shields.io/pypi/pyversions/pysharpcoredb)](https://pypi.org/project/pysharpcoredb/)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](https://opensource.org/licenses/MIT)

A high-performance Python client library for [SharpCoreDB Server](https://github.com/MPCoreDeveloper/SharpCoreDB), providing both synchronous and asynchronous APIs with automatic protocol selection and connection pooling.

## Features

- 🚀 **High Performance**: Optimized for low-latency database operations
- 🔄 **Multiple Protocols**: gRPC (primary), HTTP REST, and WebSocket support
- 🔀 **Automatic Protocol Selection**: Automatically chooses the best available protocol
- 🏊 **Connection Pooling**: Efficient connection reuse and management
- ⚡ **Async/Await**: Full asyncio support for concurrent operations
- 🔒 **Secure**: TLS encryption with certificate validation
- 📊 **Observability**: Built-in metrics and logging
- 🧪 **Well Tested**: Comprehensive test suite

## Installation

```bash
pip install pysharpcoredb
```

Or from source:

```bash
git clone https://github.com/MPCoreDeveloper/SharpCoreDB.git
cd SharpCoreDB/clients/python/pysharpcoredb
pip install -e .
```

## Quick Start

### Asynchronous API (Recommended)

```python
import asyncio
import pysharpcoredb as scdb

async def main():
    # Connect to SharpCoreDB Server
    async with scdb.connect("grpc://localhost:5001", database="mydb") as conn:
        # Execute a query
        result = await conn.execute("SELECT * FROM users WHERE age > ?", {"age": 21})
        print(f"Found {len(result)} users")

        # Insert data
        affected = await conn.execute_non_query(
            "INSERT INTO users (name, age) VALUES (?, ?)",
            {"name": "Alice", "age": 30}
        )
        print(f"Inserted {affected} rows")

        # Ping the server
        latency = await conn.ping()
        print(f"Server latency: {latency:.2f}ms")

asyncio.run(main())
```

### Synchronous API

```python
import pysharpcoredb as scdb

# Connect synchronously
with scdb.connect("https://localhost:8443", database="mydb") as conn:
    result = conn.execute("SELECT COUNT(*) as count FROM users")
    print(f"Total users: {result.rows[0]['count']}")
```

### Connection URL Formats

```python
# gRPC (recommended for performance)
conn = scdb.connect("grpc://localhost:5001")

# HTTPS REST API
conn = scdb.connect("https://localhost:8443")

# WebSocket (for streaming)
conn = scdb.connect("ws://localhost:8443")
```

## Advanced Usage

### Connection Pooling

```python
import pysharpcoredb as scdb

# Create a connection pool
pool = scdb.ConnectionPool(
    host="localhost",
    port=5001,
    database="mydb",
    min_connections=5,
    max_connections=50
)

async with pool.get_connection() as conn:
    result = await conn.execute("SELECT * FROM large_table")
```

### Streaming Queries

```python
async with scdb.connect("grpc://localhost:5001") as conn:
    # Stream results for large datasets
    async for row in conn.execute_stream("SELECT * FROM big_table"):
        process_row(row)
```

### Authentication

```python
# JWT token authentication
conn = scdb.connect(
    "grpc://localhost:5001",
    username="myuser",
    password="mypass"
)

# Or with explicit token
conn = scdb.connect(
    "grpc://localhost:5001",
    auth_token="eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
)
```

### Error Handling

```python
import pysharpcoredb as scdb
from pysharpcoredb import ConnectionError, QueryError, AuthenticationError

try:
    async with scdb.connect("grpc://localhost:5001") as conn:
        result = await conn.execute("SELECT * FROM nonexistent_table")
except ConnectionError as e:
    print(f"Connection failed: {e}")
except AuthenticationError as e:
    print(f"Authentication failed: {e}")
except QueryError as e:
    print(f"Query failed: {e.sql} - {e}")
```

## API Reference

### Connection

The main connection class providing database operations.

#### Methods

- `connect()` - Establish connection
- `disconnect()` - Close connection
- `execute(sql, parameters=None)` - Execute SELECT query
- `execute_non_query(sql, parameters=None)` - Execute INSERT/UPDATE/DELETE
- `execute_batch(statements)` - Execute multiple statements
- `ping()` - Test connection latency
- `is_connected` - Connection status property

### Connection Pool

For high-concurrency applications.

```python
pool = scdb.ConnectionPool(
    host="localhost",
    port=5001,
    database="mydb",
    min_connections=10,
    max_connections=100,
    acquire_timeout=30.0
)

async with pool.get_connection() as conn:
    # Use connection
    pass
```

## Configuration

### Environment Variables

- `SHARPCOREDB_HOST` - Default server host
- `SHARPCOREDB_PORT` - Default server port
- `SHARPCOREDB_DATABASE` - Default database name
- `SHARPCOREDB_USERNAME` - Default username
- `SHARPCOREDB_PASSWORD` - Default password

### Connection Options

- `timeout` - Connection timeout in seconds (default: 30.0)
- `tls_verify` - Verify TLS certificates (default: True)
- `ca_certs` - Path to CA certificate bundle
- `client_cert` - Path to client certificate
- `client_key` - Path to client private key

## Performance Tuning

### Protocol Selection

- **gRPC**: Best for low-latency, high-throughput operations
- **HTTP**: Good for simple queries, easier firewall traversal
- **WebSocket**: Ideal for real-time streaming and subscriptions

### Connection Pooling

```python
# For high-concurrency apps
pool = scdb.ConnectionPool(
    host="localhost",
    port=5001,
    min_connections=20,  # Keep warm connections
    max_connections=200,  # Allow burst capacity
    acquire_timeout=10.0  # Fail fast if pool exhausted
)
```

### Batch Operations

```python
# Batch multiple statements for better performance
statements = [
    "INSERT INTO users (name) VALUES ('Alice')",
    "INSERT INTO users (name) VALUES ('Bob')",
    "INSERT INTO users (name) VALUES ('Charlie')",
]

affected = await conn.execute_batch(statements)
```

## Development

### Setup Development Environment

```bash
# Clone repository
git clone https://github.com/MPCoreDeveloper/SharpCoreDB.git
cd SharpCoreDB/clients/python/pysharpcoredb

# Create virtual environment
python -m venv venv
source venv/bin/activate  # On Windows: venv\Scripts\activate

# Install dependencies
pip install -e ".[dev]"

# Run tests
pytest

# Run linter
black src/ pysharpcoredb/
isort src/ pysharpcoredb/
mypy src/pysharpcoredb/
```

### Building

```bash
# Build package
python -m build

# Install locally
pip install dist/pysharpcoredb-1.7.0.tar.gz
```

### Testing

```bash
# Run unit tests
pytest tests/unit/

# Run integration tests (requires SharpCoreDB Server)
pytest tests/integration/

# Run with coverage
pytest --cov=pysharpcoredb --cov-report=html
```

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests for new functionality
5. Ensure all tests pass
6. Submit a pull request

## License

MIT License - see [LICENSE](LICENSE) file for details.

## Support

- 📖 [Documentation](https://sharpcoredb.com/docs/python-client)
- 🐛 [Issue Tracker](https://github.com/MPCoreDeveloper/SharpCoreDB/issues)
- 💬 [Discussions](https://github.com/MPCoreDeveloper/SharpCoreDB/discussions)
- 📧 [Email Support](mailto:support@sharpcoredb.com)

---

**PySharpDB** is part of the [SharpCoreDB](https://github.com/MPCoreDeveloper/SharpCoreDB) ecosystem.
