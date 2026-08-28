"""Tests for PySharpDB connection functionality."""

import pytest
import asyncio
from unittest.mock import Mock, AsyncMock

from pysharpcoredb import Connection, ConnectionError
from pysharpcoredb.types import ConnectionInfo


class TestConnection:
    """Test Connection class."""

    @pytest.mark.asyncio
    async def test_connection_initialization(self):
        """Test connection initialization."""
        conn = Connection("localhost", 5001, "testdb")
        assert conn.host == "localhost"
        assert conn.port == 5001
        assert conn.database == "testdb"
        assert not conn.is_connected

    @pytest.mark.asyncio
    async def test_connection_context_manager(self):
        """Test connection as async context manager."""
        conn = Connection("localhost", 5001, "testdb")

        # Mock the connect and disconnect methods
        conn.connect = AsyncMock()
        conn.disconnect = AsyncMock()

        async with conn:
            assert conn.connect.called

        assert conn.disconnect.called

    @pytest.mark.asyncio
    async def test_connection_without_server(self):
        """Test Connection raises ConnectionError when all transports fail.

        The transport clients are stubbed (no live server in unit tests),
        so we simulate every protocol failing to exercise the error path.
        """
        conn = Connection("nonexistent.host", 5001, "testdb")
        conn._connect_grpc = AsyncMock(side_effect=ConnectionError("grpc unavailable"))
        conn._connect_http = AsyncMock(side_effect=ConnectionError("http unavailable"))
        conn._connect_websocket = AsyncMock(side_effect=ConnectionError("websocket unavailable"))

        with pytest.raises(ConnectionError):
            await conn.connect()

    def test_url_parsing_grpc(self):
        """Test URL parsing for gRPC connections."""
        from pysharpcoredb.connection import connect

        # This would need a mock server to test fully
        # For now, just test that the function exists
        assert callable(connect)

    def test_connection_info(self):
        """Test ConnectionInfo dataclass."""
        info = ConnectionInfo(
            database_name="testdb",
            session_id="session123",
            server_version="1.5.0"
        )
        assert info.database_name == "testdb"
        assert info.session_id == "session123"
        assert info.server_version == "1.5.0"
