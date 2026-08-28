"""PySharpDB - Python client for SharpCoreDB Server.

A high-performance Python client library for SharpCoreDB Server with support for
gRPC, HTTP REST, and WebSocket protocols. Provides both synchronous and
asynchronous APIs with automatic connection pooling and protocol selection.

Example:
    >>> import pysharpcoredb as scdb
    >>> async with scdb.connect("localhost:5001") as conn:
    ...     result = await conn.execute("SELECT * FROM users")
    ...     print(result.rows)
"""

__version__ = "1.9.6"
__author__ = "MPCoreDeveloper"
__license__ = "MIT"

from .connection import Connection, connect
from .exceptions import SharpCoreDBError, ConnectionError, AuthenticationError, QueryError, ConfigurationError, TimeoutError
from .types import Row, ResultSet
from .pool import ConnectionPool, create_pool

__all__ = [
    "Connection",
    "connect",
    "ConnectionPool",
    "create_pool",
    "SharpCoreDBError",
    "ConnectionError",
    "AuthenticationError",
    "QueryError",
    "ConfigurationError",
    "TimeoutError",
    "Row",
    "ResultSet",
]
